#!/usr/bin/env python3
"""Vision-to-Wall hand sensor: webcam -> MediaPipe HandLandmarker / PoseLandmarker -> UDP motion packets.

Sends the Core MotionProtocol packet every processed frame (count 0 when no moving hand),
so Unity in sensorMode "external" works without any code change.
Body position goes to udpBodyPort as a separate stream in the same format (count 0 or 1, own seq).
Pose joints (13) go to udpPosePort (PoseProtocol, pose_stream.py) on every frame where pose ran.

Usage:
  python hand_sensor.py [--config PATH] [--points palm|tips|all] [--speed-ref 1.5] [--min-speed 0.05]
                        [--emit-still] [--preview] [--quiet] [--model models/hand_landmarker.task]
                        [--no-body] [--pose-model models/pose_landmarker_lite.task]
                        [--exposure -7|auto] [--stop-file PATH] [--status-file PATH]

Exposure: --exposure wins over config cameraExposure ("-7" / "auto" / "" = camera as is).
Manual exposure is set back to auto on a normal stop: q / Esc in the preview, Ctrl+C, Ctrl+Break, closing the
console window, --duration, or the --stop-file appearing (the launcher uses that). A hard kill cannot restore it.
"""
import argparse
import json
import os
import socket
import sys
import threading
import time

import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_tasks
from mediapipe.tasks.python import vision

import motion_packet as mpk
import pose_record
import pose_stream as pst
from body_tracker import BodyTracker, PoseThrottle
from hand_tracker import STILL_INTENSITY, HandTracker
import sensor_config as scfg

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_CONFIG = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "StreamingAssets", "config.json"))
DEFAULT_MODEL = os.path.join(HERE, "models", "hand_landmarker.task")
DEFAULT_POSE_MODEL = os.path.join(HERE, "models", "pose_landmarker_lite.task")
RECORD_DIR = os.path.join(HERE, "recordings")

CONFIG_POLL_SEC = 0.5             # how often config.json mtime is checked for live tuning changes
NUM_HANDS = 2
CONFIDENCE = 0.5
CAMERA_RETRY_SEC = 2.0
CAMERA_MAX_READ_FAILS = 30        # consecutive failed reads -> reopen camera
LOG_INTERVAL_SEC = 1.0
CAP_AUTO_EXPOSURE_ON = 0.75       # OpenCV DSHOW values for CAP_PROP_AUTO_EXPOSURE
CAP_AUTO_EXPOSURE_OFF = 0.25
STOP_POLL_SEC = 0.25              # --stop-file check interval
CLOSE_WAIT_SEC = 4.0              # console close: wait this long for cleanup (Windows kills after about 5 s)
PREVIEW_WINDOW = "HandSensor preview"
KEY_ENTER = (13, 10)


# ---------------------------------------------------------------- config

def load_config(path, tuning, pinned):
    """Returns (startup cfg, tuning). Missing / broken file -> defaults."""
    data, err = scfg.read_json(path)
    if data is None:
        print("[config] %s (%s) -> defaults" % (err, path), flush=True)
    cfg, warnings = scfg.parse_startup(data)
    tuning, _changed, tw = scfg.merge_tuning(data, tuning, pinned)
    for w in warnings + tw:
        print("[config] " + w, flush=True)
    mode = data.get("sensorMode") if data else None
    if mode is not None and mode != "external":
        print("[config] WARNING sensorMode=%r - Unity will also open the webcam. Set \"external\"." % mode,
              flush=True)
    return cfg, tuning


def format_tuning(t):
    return "speedRef=%.2f minSpeed=%.3f emitStill=%s" % (t["handSpeedRef"], t["handMinSpeed"], t["handEmitStill"])


# ---------------------------------------------------------------- camera

def apply_exposure(cap, exposure):
    """exposure: None = leave as is, "auto", or manual log2 seconds. The driver keeps it after the camera closes."""
    if exposure is None:
        return
    if exposure == "auto":
        cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, CAP_AUTO_EXPOSURE_ON)
    else:
        cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, CAP_AUTO_EXPOSURE_OFF)
        cap.set(cv2.CAP_PROP_EXPOSURE, exposure)
    print("[camera] exposure %s" % (exposure,), flush=True)


def open_camera(index, width, height, should_stop, exposure=None):
    """Open the webcam, retrying every CAMERA_RETRY_SEC. Returns None if should_stop() becomes true."""
    attempt = 0
    while not should_stop():
        attempt += 1
        cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        if cap.isOpened():
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
            apply_exposure(cap, exposure)
            ok, frame = cap.read()
            if ok and frame is not None:
                h, w = frame.shape[:2]
                print("[camera] opened index %d at %dx%d (requested %dx%d)" % (index, w, h, width, height),
                      flush=True)
                return cap
        cap.release()
        print("[camera] open failed (index %d, attempt %d) - retry in %.0fs" % (index, attempt, CAMERA_RETRY_SEC),
              flush=True)
        retry_at = time.perf_counter() + CAMERA_RETRY_SEC
        while time.perf_counter() < retry_at and not should_stop():  # stop requests are seen while waiting
            time.sleep(0.1)
    return None


# ---------------------------------------------------------------- stop / status (launcher)

class ConsoleStop:
    """Windows console events (Ctrl+C, Ctrl+Break, window close, logoff, shutdown) -> graceful stop.

    The handler runs on a system thread. It sets the stop flag and, for close / logoff / shutdown (where Windows
    ends the process as soon as the handler returns), waits until main() finished cleanup so the camera exposure is
    restored. No-op on other platforms.
    """

    CTRL_C, CTRL_BREAK, CTRL_CLOSE, CTRL_LOGOFF, CTRL_SHUTDOWN = 0, 1, 2, 5, 6
    NAMES = {0: "Ctrl+C", 1: "Ctrl+Break", 2: "console close", 5: "logoff", 6: "shutdown"}

    def __init__(self, state):
        self._state = state
        self.cleaned = threading.Event()
        self.event_name = None
        self._handler = None
        if os.name != "nt":
            return
        import ctypes
        from ctypes import wintypes
        routine = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.DWORD)

        def handler(ctrl_type):
            self.event_name = self.NAMES.get(ctrl_type, str(ctrl_type))
            self._state["stop"] = True
            if ctrl_type in (self.CTRL_CLOSE, self.CTRL_LOGOFF, self.CTRL_SHUTDOWN):
                self.cleaned.wait(CLOSE_WAIT_SEC)
            return True  # handled: no KeyboardInterrupt, the main loop ends on the flag

        self._handler = routine(handler)  # keep a reference: ctypes callbacks must outlive registration
        ctypes.windll.kernel32.SetConsoleCtrlHandler(self._handler, True)


class StatusFile:
    """Small JSON heartbeat for the launcher: {"pid", "state", "fps", "frames", "exposure", "time"}.

    state: starting -> camera (waiting for the camera) -> running -> stopped. Written atomically (temp + replace),
    at most once per LOG_INTERVAL_SEC unless the state changes. Disabled when path is None.
    """

    def __init__(self, path):
        self.path = path
        self.state = None
        self._next = 0.0

    def write(self, state, fps=0.0, frames=0, exposure=None, force=False):
        if self.path is None:
            return
        now = time.perf_counter()
        if not force and state == self.state and now < self._next:
            return
        self.state = state
        self._next = now + LOG_INTERVAL_SEC
        data = {"pid": os.getpid(), "state": state, "fps": round(fps, 1), "frames": frames,
                "exposure": "" if exposure is None else str(exposure), "time": time.time()}
        tmp = self.path + ".tmp"
        try:
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(data, f)
            os.replace(tmp, self.path)
        except OSError:
            pass  # the launcher treats a stale file as "not ready"; never stop the sensor for this


# ---------------------------------------------------------------- preview

def draw_preview(frame, result, preview_pts, body_center, mirror, fps, seq, recording=None):
    h, w = frame.shape[:2]
    img = frame.copy()
    for lms in result.hand_landmarks:
        for c in vision.HandLandmarksConnections.HAND_CONNECTIONS:
            a, b = lms[c.start], lms[c.end]
            cv2.line(img, (int(a.x * w), int(a.y * h)), (int(b.x * w), int(b.y * h)), (200, 200, 200), 1)
        for lm in lms:
            cv2.circle(img, (int(lm.x * w), int(lm.y * h)), 3, (0, 200, 255), -1)
    for px, py, v, still in preview_pts:
        color = (150, 150, 150) if still else (0, 255, 0)
        cv2.circle(img, (int(px * w), int(py * h)), int(6 + 24 * v), color, 2)
    if body_center is not None:
        bx, by = int(body_center[0] * w), int(body_center[1] * h)
        cv2.drawMarker(img, (bx, by), (255, 0, 255), cv2.MARKER_CROSS, 40, 3)
    if mirror:
        img = cv2.flip(img, 1)  # display only; coordinates are mirrored in to_output_coords
    cv2.putText(img, "fps %.1f  hands %d  points %d  seq %d" % (fps, len(result.hand_landmarks),
                len(preview_pts), seq), (10, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)
    if recording is not None:
        cv2.circle(img, (w - 110, 22), 8, (0, 0, 255), -1)
        cv2.putText(img, "REC %d" % recording, (w - 96, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
    cv2.putText(img, "Enter: record joints   q / Esc: quit", (10, h - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.5,
                (255, 255, 255), 1)
    cv2.imshow(PREVIEW_WINDOW, img)


# ---------------------------------------------------------------- pose

def create_pose_landmarker(model_path):
    options = vision.PoseLandmarkerOptions(
        base_options=mp_tasks.BaseOptions(model_asset_path=model_path),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=1,
        min_pose_detection_confidence=CONFIDENCE,
        min_pose_presence_confidence=CONFIDENCE,
        min_tracking_confidence=CONFIDENCE,
    )
    return vision.PoseLandmarker.create_from_options(options)


def pose_landmarks(result):
    """First detected pose as [(x, y, visibility)], or None."""
    if not result.pose_landmarks:
        return None
    return [(lm.x, lm.y, lm.visibility) for lm in result.pose_landmarks[0]]


def pose_world_landmarks(result):
    """First pose's world landmarks as [(x, y, z)] (meters, hip centered), or None when the model gives none."""
    if not getattr(result, "pose_world_landmarks", None):
        return None
    return [(lm.x, lm.y, lm.z) for lm in result.pose_world_landmarks[0]]


# ---------------------------------------------------------------- main

def parse_args(argv):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--config", default=DEFAULT_CONFIG, help="config.json path (default: Assets/StreamingAssets)")
    p.add_argument("--points", choices=mpk.POINT_MODES, default="palm")
    p.add_argument("--speed-ref", type=float, default=None,
                   help="speed (screen widths/s) that maps to v=1. Default: config handSpeedRef or 1.5. "
                        "Given here = fixed (config changes ignored)")
    p.add_argument("--min-speed", type=float, default=None,
                   help="hands slower than this are not sent. Default: config handMinSpeed or 0.05")
    p.add_argument("--emit-still", action="store_true",
                   help="send still hands with v=%.1f (default: config handEmitStill)" % STILL_INTENSITY)
    p.add_argument("--preview", action="store_true", help="show OpenCV debug window")
    p.add_argument("--quiet", action="store_true", help="no 1 s log line")
    p.add_argument("--model", default=DEFAULT_MODEL)
    p.add_argument("--no-body", action="store_true",
                   help="hand stream only: no pose, nothing sent to udpBodyPort / udpPosePort")
    p.add_argument("--pose-model", default=DEFAULT_POSE_MODEL)
    p.add_argument("--exposure", type=scfg.parse_exposure, default=None,
                   help="manual exposure as log2 seconds (-7 = 1/128 s) for less motion blur, or auto. "
                        "Default: leave the camera as is. Manual is set back to auto on exit")
    p.add_argument("--duration", type=float, default=0.0, help="stop after N seconds (0 = until Ctrl+C)")
    p.add_argument("--stop-file", default=None,
                   help="stop normally (exposure restored) when this file exists; deleted at start and stop")
    p.add_argument("--status-file", default=None,
                   help="write a JSON heartbeat here every second (launcher readiness / hang check)")
    args = p.parse_args(argv)
    if args.speed_ref is not None and args.speed_ref <= 0:
        p.error("--speed-ref must be > 0")
    if args.min_speed is not None and args.min_speed < 0:
        p.error("--min-speed must be >= 0")
    return args


def reload_tuning(watcher, tuning, pinned, cfg, tracker):
    """Apply tuning changes when config.json was saved. Startup fields only warn (restart needed)."""
    data, err = watcher.poll()
    if data is None:
        if err:
            print("[config] reload skipped (%s) - retry" % err, flush=True)
        return tuning
    new, changed, warnings = scfg.merge_tuning(data, tuning, pinned)
    for w in warnings:
        print("[config] " + w, flush=True)
    if changed:
        tracker.apply_tuning(new)
        print("[config] reloaded: %s" % format_tuning(new), flush=True)
    startup, _ = scfg.parse_startup(data)
    moved = [k for k in scfg.STARTUP_DEFAULTS if startup[k] != cfg[k]]
    if moved:
        print("[config] %s changed - restart the sensor to apply" % ", ".join(moved), flush=True)
        cfg.update({k: startup[k] for k in moved})  # warn once per change
    return new


def cli_tuning(args):
    """Defaults overridden by CLI options. Returns (tuning, pinned keys)."""
    tuning = dict(scfg.TUNING_DEFAULTS)
    pinned = set()
    if args.speed_ref is not None:
        tuning["handSpeedRef"] = args.speed_ref
        pinned.add("handSpeedRef")
    if args.min_speed is not None:
        tuning["handMinSpeed"] = args.min_speed
        pinned.add("handMinSpeed")
    if args.emit_still:
        tuning["handEmitStill"] = True
        pinned.add("handEmitStill")
    return tuning, pinned


def main(argv=None):
    args = parse_args(argv)
    if not os.path.isfile(args.model):
        print("model not found: %s\nrun: python download_model.py" % args.model, file=sys.stderr)
        return 1

    tuning, pinned = cli_tuning(args)
    cfg, tuning = load_config(args.config, tuning, pinned)
    exposure, exposure_src = scfg.resolve_exposure(args.exposure, cfg["cameraExposure"])
    print("[camera] exposure setting: %s" % ("camera as is" if exposure is None else
                                             "%s (%s)" % (exposure, exposure_src)), flush=True)
    watcher = scfg.ConfigWatcher(args.config)
    target = (cfg["udpIp"], cfg["udpPort"])
    print("hand_sensor -> %s:%d  points=%s mirror=%s %s%s"
          % (target[0], target[1], args.points, cfg["mirror"], format_tuning(tuning),
             "  (fixed by CLI: %s)" % ", ".join(sorted(pinned)) if pinned else ""),
          flush=True)

    options = vision.HandLandmarkerOptions(
        base_options=mp_tasks.BaseOptions(model_asset_path=args.model),
        running_mode=vision.RunningMode.VIDEO,
        num_hands=NUM_HANDS,
        min_hand_detection_confidence=CONFIDENCE,
        min_hand_presence_confidence=CONFIDENCE,
        min_tracking_confidence=CONFIDENCE,
    )
    tracker = HandTracker(args.points, cfg["mirror"], tuning)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    use_body = not args.no_body
    if use_body and not os.path.isfile(args.pose_model):
        # Unity falls back to the motion centroid, so the hand stream alone stays playable
        print("[body] model not found: %s - hand stream only. run: python download_model.py" % args.pose_model,
              flush=True)
        use_body = False
    body_target = (cfg["udpIp"], cfg["udpBodyPort"])
    print("body stream -> %s" % ("%s:%d" % body_target if use_body else "off"), flush=True)
    pose = create_pose_landmarker(args.pose_model) if use_body else None
    body_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    body_tracker = BodyTracker(cfg["mirror"])
    throttle = PoseThrottle()
    body_point = None
    body_center = None
    body_seq = 0
    pose_target = (cfg["udpIp"], cfg["udpPosePort"])
    pose_streamer = pst.PoseStreamer(cfg["mirror"]) if use_body and cfg["udpPosePort"] else None
    print("pose joint stream -> %s" % ("%s:%d" % pose_target if pose_streamer else "off"), flush=True)
    pose_count = 0             # joints in the last pose packet (log)
    world_missing = False
    recorder = pose_record.PoseRecorder()

    def stop_recording():
        path, rows = recorder.stop(RECORD_DIR)
        print("[record] saved %s" % path, flush=True)
        for line in pose_record.report(rows)[1]:
            print("[record] " + line, flush=True)

    start = time.perf_counter()
    state = {"stop": False, "reason": None, "next_stop_poll": 0.0}
    console = ConsoleStop(state)
    status = StatusFile(args.status_file)
    status.write("starting", exposure=exposure, force=True)
    if args.stop_file and os.path.exists(args.stop_file):
        os.remove(args.stop_file)  # a leftover from the last run must not stop this one

    def should_stop():
        if state["stop"]:
            return True
        now = time.perf_counter()
        if args.duration > 0 and now - start >= args.duration:
            state["reason"] = "duration"
            return True
        if args.stop_file and now >= state["next_stop_poll"]:
            state["next_stop_poll"] = now + STOP_POLL_SEC
            if os.path.exists(args.stop_file):
                state["reason"] = "stop file"
                state["stop"] = True
                return True
        return False

    seq = 0
    frames = 0
    send_errors = 0
    read_fails = 0
    last_ts = -1
    win_frames = 0
    win_start = start
    fps = 0.0
    fps_sum = 0.0
    fps_samples = 0
    next_config_poll = start + CONFIG_POLL_SEC
    cap = None

    try:
        with vision.HandLandmarker.create_from_options(options) as landmarker:
            status.write("camera", exposure=exposure, force=True)
            cap = open_camera(cfg["cameraIndex"], cfg["cameraWidth"], cfg["cameraHeight"], should_stop, exposure)
            while cap is not None and not should_stop():
                ok, frame = cap.read()
                if not ok or frame is None:
                    read_fails += 1
                    if read_fails >= CAMERA_MAX_READ_FAILS:
                        print("[camera] %d read failures - reopening" % read_fails, flush=True)
                        cap.release()
                        read_fails = 0
                        tracker.reset()
                        body_tracker.reset()
                        body_point = body_center = None
                        status.write("camera", exposure=exposure, force=True)
                        cap = open_camera(cfg["cameraIndex"], cfg["cameraWidth"], cfg["cameraHeight"], should_stop,
                                          exposure)
                    continue
                read_fails = 0

                now = time.perf_counter()
                capture_ms = int(time.monotonic() * 1000)  # pose packet timestamp = frame capture time
                if frames == 0:
                    win_start = now  # exclude model load / camera open from the first fps window
                if now >= next_config_poll:
                    next_config_poll = now + CONFIG_POLL_SEC
                    tuning = reload_tuning(watcher, tuning, pinned, cfg, tracker)
                ts = max(int((now - start) * 1000), last_ts + 1)  # VIDEO mode needs increasing timestamps
                last_ts = ts
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                hand_start = time.perf_counter()
                result = landmarker.detect_for_video(image, ts)
                hand_ms = (time.perf_counter() - hand_start) * 1000.0

                hands = []
                for i, lms in enumerate(result.hand_landmarks):
                    label = result.handedness[i][0].category_name if i < len(result.handedness) and \
                        result.handedness[i] else ""
                    hands.append(([(lm.x, lm.y) for lm in lms], label))
                h, w = frame.shape[:2]
                points, preview_pts = tracker.update(hands, now, h / float(w))

                seq += 1
                try:
                    sock.sendto(mpk.pack_packet(seq, int(time.monotonic() * 1000), points), target)
                except OSError:
                    send_errors += 1  # e.g. ICMP port unreachable on Windows when Unity is not listening

                if pose is not None:
                    if throttle.should_run():  # a skipped frame re-sends the previous value
                        pose_start = time.perf_counter()
                        pose_result = pose.detect_for_video(image, ts)
                        throttle.add_inference(hand_ms + (time.perf_counter() - pose_start) * 1000.0)
                        screen_lms = pose_landmarks(pose_result)
                        body_point, body_center = body_tracker.update(screen_lms, now, h / float(w))
                        if pose_streamer is not None:
                            world_lms = pose_world_landmarks(pose_result) if screen_lms is not None else None
                            if screen_lms is not None and world_lms is None and not world_missing:
                                world_missing = True
                                print("[pose] model gives no world landmarks - world values sent as 0", flush=True)
                            packet = pose_streamer.update(screen_lms, world_lms, capture_ms)
                            if packet is not None:  # throttle-skipped frames send nothing on this port
                                pose_count = 0 if screen_lms is None else pst.JOINT_COUNT
                                if recorder.active:
                                    recorder.add(capture_ms, pst.unpack_packet(packet)[2])
                                try:
                                    body_sock.sendto(packet, pose_target)
                                except OSError:
                                    send_errors += 1
                    body_seq += 1
                    try:
                        body_sock.sendto(mpk.pack_packet(body_seq, int(time.monotonic() * 1000),
                                                         [body_point] if body_point else []), body_target)
                    except OSError:
                        send_errors += 1

                frames += 1
                win_frames += 1
                status.write("running", fps, frames, exposure)
                if now - win_start >= LOG_INTERVAL_SEC:
                    fps = win_frames / (now - win_start)
                    fps_sum += fps
                    fps_samples += 1
                    if not args.quiet:
                        print("fps=%.1f hands=%d points=%d seq=%d held=%d body=%d poseSkip=%d inferMs=%.1f joints=%d"
                              % (fps, len(hands), len(points), seq, tracker.held_frames,
                                 1 if body_point else 0, 1 if throttle.active else 0, throttle.average_ms,
                                 pose_count), flush=True)
                    win_frames = 0
                    tracker.held_frames = 0
                    win_start = now

                if args.preview:
                    draw_preview(frame, result, preview_pts, body_center, cfg["mirror"], fps, seq,
                                 len(recorder.rows) if recorder.active else None)
                    key = cv2.waitKey(1) & 0xFF
                    if key in KEY_ENTER:
                        if recorder.active:
                            stop_recording()
                        elif pose_streamer is None:
                            print("[record] pose joint stream is off - nothing to record", flush=True)
                        else:
                            recorder.start(capture_ms)
                            print("[record] started - press Enter again to stop", flush=True)
                    if key in (ord("q"), 27) or cv2.getWindowProperty(PREVIEW_WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                        state["reason"] = "preview"
                        state["stop"] = True
    except KeyboardInterrupt:
        state["reason"] = "Ctrl+C"
    finally:
        if recorder.active:
            stop_recording()
        if cap is not None:
            if exposure not in (None, "auto"):
                apply_exposure(cap, "auto")
                cap.read()  # the driver applies the change on the next frame
            cap.release()
        sock.close()
        body_sock.close()
        if pose is not None:
            pose.close()
        if args.preview:
            cv2.destroyAllWindows()

        if args.stop_file and os.path.exists(args.stop_file):
            try:
                os.remove(args.stop_file)
            except OSError:
                pass

    elapsed = time.perf_counter() - start
    avg = fps_sum / fps_samples if fps_samples else 0.0
    reason = console.event_name or state["reason"] or "end"
    print("stopped (%s): %.1fs, frames=%d, packets=%d, body packets=%d, pose packets=%d, avg fps=%.1f, send errors=%d"
          % (reason, elapsed, frames, seq, body_seq, pose_streamer.seq if pose_streamer else 0, avg, send_errors),
          flush=True)
    status.write("stopped", 0.0, frames, exposure, force=True)
    console.cleaned.set()
    return 0


if __name__ == "__main__":
    sys.exit(main())
