#!/usr/bin/env python3
"""Vision-to-Wall hand sensor: webcam -> MediaPipe HandLandmarker / PoseLandmarker -> UDP motion packets.

Sends the Core MotionProtocol packet every processed frame (count 0 when no moving hand),
so Unity in sensorMode "external" works without any code change.
Body position goes to udpBodyPort as a separate stream in the same format (count 0 or 1, own seq).

Usage:
  python hand_sensor.py [--config PATH] [--points palm|tips|all] [--speed-ref 1.5] [--min-speed 0.05]
                        [--emit-still] [--preview] [--quiet] [--model models/hand_landmarker.task]
                        [--no-body] [--pose-model models/pose_landmarker_lite.task]
"""
import argparse
import os
import socket
import sys
import time

import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_tasks
from mediapipe.tasks.python import vision

import motion_packet as mpk
from body_tracker import BodyTracker, PoseThrottle
from hand_tracker import STILL_INTENSITY, HandTracker
import sensor_config as scfg

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_CONFIG = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "StreamingAssets", "config.json"))
DEFAULT_MODEL = os.path.join(HERE, "models", "hand_landmarker.task")
DEFAULT_POSE_MODEL = os.path.join(HERE, "models", "pose_landmarker_lite.task")

CONFIG_POLL_SEC = 0.5             # how often config.json mtime is checked for live tuning changes
NUM_HANDS = 2
CONFIDENCE = 0.5
CAMERA_RETRY_SEC = 2.0
CAMERA_MAX_READ_FAILS = 30        # consecutive failed reads -> reopen camera
LOG_INTERVAL_SEC = 1.0
PREVIEW_WINDOW = "HandSensor preview"


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

def open_camera(index, width, height, should_stop):
    """Open the webcam, retrying every CAMERA_RETRY_SEC. Returns None if should_stop() becomes true."""
    attempt = 0
    while not should_stop():
        attempt += 1
        cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        if cap.isOpened():
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
            ok, frame = cap.read()
            if ok and frame is not None:
                h, w = frame.shape[:2]
                print("[camera] opened index %d at %dx%d (requested %dx%d)" % (index, w, h, width, height),
                      flush=True)
                return cap
        cap.release()
        print("[camera] open failed (index %d, attempt %d) - retry in %.0fs" % (index, attempt, CAMERA_RETRY_SEC),
              flush=True)
        time.sleep(CAMERA_RETRY_SEC)
    return None


# ---------------------------------------------------------------- preview

def draw_preview(frame, result, preview_pts, body_center, mirror, fps, seq):
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
    cv2.putText(img, "q / Esc: quit", (10, h - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
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
    p.add_argument("--no-body", action="store_true", help="hand stream only: no pose, nothing sent to udpBodyPort")
    p.add_argument("--pose-model", default=DEFAULT_POSE_MODEL)
    p.add_argument("--duration", type=float, default=0.0, help="stop after N seconds (0 = until Ctrl+C)")
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

    start = time.perf_counter()
    state = {"stop": False}

    def should_stop():
        return state["stop"] or (args.duration > 0 and time.perf_counter() - start >= args.duration)

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
            cap = open_camera(cfg["cameraIndex"], cfg["cameraWidth"], cfg["cameraHeight"], should_stop)
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
                        cap = open_camera(cfg["cameraIndex"], cfg["cameraWidth"], cfg["cameraHeight"], should_stop)
                    continue
                read_fails = 0

                now = time.perf_counter()
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
                        body_point, body_center = body_tracker.update(pose_landmarks(pose_result), now, h / float(w))
                    body_seq += 1
                    try:
                        body_sock.sendto(mpk.pack_packet(body_seq, int(time.monotonic() * 1000),
                                                         [body_point] if body_point else []), body_target)
                    except OSError:
                        send_errors += 1

                frames += 1
                win_frames += 1
                if now - win_start >= LOG_INTERVAL_SEC:
                    fps = win_frames / (now - win_start)
                    fps_sum += fps
                    fps_samples += 1
                    if not args.quiet:
                        print("fps=%.1f hands=%d points=%d seq=%d held=%d body=%d poseSkip=%d inferMs=%.1f"
                              % (fps, len(hands), len(points), seq, tracker.held_frames,
                                 1 if body_point else 0, 1 if throttle.active else 0, throttle.average_ms),
                              flush=True)
                    win_frames = 0
                    tracker.held_frames = 0
                    win_start = now

                if args.preview:
                    draw_preview(frame, result, preview_pts, body_center, cfg["mirror"], fps, seq)
                    key = cv2.waitKey(1) & 0xFF
                    if key in (ord("q"), 27) or cv2.getWindowProperty(PREVIEW_WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                        state["stop"] = True
    except KeyboardInterrupt:
        pass
    finally:
        if cap is not None:
            cap.release()
        sock.close()
        body_sock.close()
        if pose is not None:
            pose.close()
        if args.preview:
            cv2.destroyAllWindows()

    elapsed = time.perf_counter() - start
    avg = fps_sum / fps_samples if fps_samples else 0.0
    print("stopped: %.1fs, frames=%d, packets=%d, body packets=%d, avg fps=%.1f, send errors=%d"
          % (elapsed, frames, seq, body_seq, avg, send_errors), flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
