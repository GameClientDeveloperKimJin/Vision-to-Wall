#!/usr/bin/env python3
"""Guided comparison recording for the combat avatar (R4b, Assets/05_Docs/57_세션R4b_AvatarTuning.md).

Opens the camera, runs the same pose processing as hand_sensor.py (hand_sensor.create_pose_landmarker,
pose_landmarks / pose_world_landmarks, pose_stream.extract_joints) and walks the player through a fixed motion
script shown on the preview (Korean, "pose -> motion -> seconds"). Saves, per take, under recordings/ (git ignored):

  recordings/compare_YYYYMMDD_HHMMSS/
    joints.csv     recording CSV of pose_record.py (what the sensor sends on udpPosePort), t_ms from the first frame
    frames.csv     frame index, t_ms (camera capture time, same clock as joints.csv)
    segments.csv   step, id, label, start_ms, end_ms
    frames/NNNNN.jpg  camera frames (NOT mirrored). Contain the player's face: local only, delete after analysis

Unity replays joints.csv through the avatar (menu VisionToWall/Avatar/Compare Recording) and writes avatar.csv;
pose_compare_sheet.py then builds the side-by-side images and the mismatch list.

The hand sensor must not be running (one camera). Differences from the live sensor: pose runs on every frame
(no PoseThrottle), no hand landmarker. --send also forwards the joints to udpPosePort like the sensor.

Usage:
  python pose_compare_record.py [--exposure -7] [--send] [--out recordings/compare_x]
Keys: Space = start now (skips the countdown), q / Esc = stop (what was recorded is kept).
"""
import argparse
import csv
import os
import socket
import sys
import time

import pose_record
import pose_stream as pst
import sensor_config as scfg

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_CONFIG = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "StreamingAssets", "config.json"))
DEFAULT_POSE_MODEL = os.path.join(HERE, "models", "pose_landmarker_lite.task")
RECORD_DIR = os.path.join(HERE, "recordings")
WINDOW = "Avatar compare recording"
FONT = r"C:\Windows\Fonts\malgun.ttf"
COUNTDOWN_SEC = 5.0
JPEG_QUALITY = 85

# (seconds, id, label shown to the player: pose -> motion). Doc 57 section 1 table.
STEPS = (
    (4.0, "stand", "정면을 보고 차렷 → 가만히"),
    (6.0, "raise_right_slow", "차렷 → 오른팔을 옆으로 천천히 머리 위까지 들었다 내리기"),
    (6.0, "raise_left_slow", "차렷 → 왼팔을 옆으로 천천히 머리 위까지 들었다 내리기"),
    (5.0, "raise_right_fast", "차렷 → 오른팔을 옆으로 빠르게 3번 들었다 내리기"),
    (5.0, "raise_left_fast", "차렷 → 왼팔을 옆으로 빠르게 3번 들었다 내리기"),
    (6.0, "reach_forward", "차렷 → 두 팔을 카메라 쪽으로 쭉 뻗었다 내리기 (2번)"),
    (5.0, "cross_arms", "차렷 → 가슴 앞에서 두 팔을 X자로 교차하고 버티기"),
    (5.0, "arms_overhead", "차렷 → 두 팔을 머리 위로 쭉 올리고 버티기"),
    (5.0, "wave_fast", "오른팔 든 채 → 손을 좌우로 빠르게 흔들기"),
    (8.0, "step_sides", "차렷 → 왼쪽으로 두 걸음, 오른쪽으로 두 걸음 (화면 안에서)"),
    (6.0, "lean", "차렷 → 상체를 왼쪽, 오른쪽으로 기울이기"),
    (6.0, "squat", "차렷 → 살짝 앉았다 일어나기 (2번)"),
    (3.0, "stand_end", "차렷 → 가만히 (끝)"),
)


def total_sec(steps=STEPS):
    return sum(s[0] for s in steps)


def step_at(t_sec, steps=STEPS):
    """(index, seconds left in the step) for time t_sec since the script start, or (len(steps), 0) after the end."""
    start = 0.0
    for i, (dur, _id, _label) in enumerate(steps):
        if t_sec < start + dur:
            return i, start + dur - t_sec
        start += dur
    return len(steps), 0.0


def segments(script_start_ms, steps=STEPS):
    """[(step, id, label, start_ms, end_ms)] with times on the recording clock."""
    out = []
    t = float(script_start_ms)
    for i, (dur, sid, label) in enumerate(steps):
        out.append((i, sid, label, int(round(t)), int(round(t + dur * 1000.0))))
        t += dur * 1000.0
    return out


def write_segments(path, segs):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["step", "id", "label", "start_ms", "end_ms"])
        for row in segs:
            w.writerow(row)


def write_frames(path, frames):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["frame", "t_ms"])
        for i, t in frames:
            w.writerow([i, t])


def _guide(img, lines, font_cache):
    """Draws Korean lines on a BGR image with PIL (OpenCV fonts are ASCII only)."""
    import numpy as np
    from PIL import Image, ImageDraw, ImageFont

    if "font" not in font_cache:
        font_cache["font"] = ImageFont.truetype(FONT, 22) if os.path.isfile(FONT) else ImageFont.load_default()
        font_cache["small"] = ImageFont.truetype(FONT, 16) if os.path.isfile(FONT) else ImageFont.load_default()
    pil = Image.fromarray(img[:, :, ::-1])
    draw = ImageDraw.Draw(pil)
    y = 8
    for i, (text, color) in enumerate(lines):
        font = font_cache["font"] if i == 0 else font_cache["small"]
        draw.rectangle((0, y - 2, pil.width, y + (28 if i == 0 else 22)), fill=(0, 0, 0))
        draw.text((10, y), text, font=font, fill=color)
        y += 32 if i == 0 else 24
    return np.asarray(pil)[:, :, ::-1].copy()


def parse_args(argv):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--config", default=DEFAULT_CONFIG)
    p.add_argument("--pose-model", default=DEFAULT_POSE_MODEL)
    p.add_argument("--exposure", type=scfg.parse_exposure, default=None, help="as hand_sensor.py (-7 or auto)")
    p.add_argument("--out", default=None, help="take folder (default recordings/compare_YYYYMMDD_HHMMSS)")
    p.add_argument("--send", action="store_true", help="also send the joints to udpPosePort like the sensor")
    p.add_argument("--countdown", type=float, default=COUNTDOWN_SEC)
    return p.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    import cv2
    import mediapipe as mp
    import hand_sensor as hs

    data, _err = scfg.read_json(args.config)
    cfg, _warnings = scfg.parse_startup(data)
    mirror = cfg["mirror"]
    out = args.out or os.path.join(RECORD_DIR, time.strftime("compare_%Y%m%d_%H%M%S"))
    frames_dir = os.path.join(out, "frames")
    os.makedirs(frames_dir, exist_ok=True)
    print("[compare] saving to %s (camera frames contain your face: local only, delete after analysis)" % out,
          flush=True)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM) if args.send else None
    streamer = pst.PoseStreamer(mirror) if args.send else None
    pose_target = (cfg["udpIp"], cfg["udpPosePort"])
    landmarker = hs.create_pose_landmarker(args.pose_model)
    state = {"stop": False}
    cap = hs.open_camera(cfg["cameraIndex"], cfg["cameraWidth"], cfg["cameraHeight"], lambda: state["stop"],
                         args.exposure)
    if cap is None:
        return 1

    rows, frame_rows = [], []
    t0 = None
    script_start = None
    countdown_start = time.monotonic()
    last_ts = -1
    font_cache = {}
    total = total_sec()
    try:
        while not state["stop"]:
            ok, frame = cap.read()
            if not ok or frame is None:
                continue
            capture_ms = int(time.monotonic() * 1000)
            if t0 is None:
                t0 = capture_ms
            t = capture_ms - t0
            ts = max(t, last_ts + 1)
            last_ts = ts

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)
            screen = hs.pose_landmarks(result)
            world = hs.pose_world_landmarks(result) if screen is not None else None
            joints = pst.extract_joints(screen, world, mirror) if screen is not None else []
            index = len(frame_rows)
            cv2.imwrite(os.path.join(frames_dir, "%05d.jpg" % index), frame, [cv2.IMWRITE_JPEG_QUALITY, JPEG_QUALITY])
            frame_rows.append((index, t))
            rows.append((t, joints))
            if streamer is not None:
                packet = streamer.update(screen, world, capture_ms)
                if packet is not None:
                    try:
                        sock.sendto(packet, pose_target)
                    except OSError:
                        pass

            # guide
            now = time.monotonic()
            view = cv2.flip(frame, 1) if mirror else frame.copy()
            h, w = view.shape[:2]
            for sx, sy, _wx, _wy, _wz, v in joints:
                if v >= 0.5:
                    cv2.circle(view, (int(sx * w), int((1.0 - sy) * h)), 5, (0, 255, 0), -1)
            if script_start is None:
                left = args.countdown - (now - countdown_start)
                lines = [("준비: 전신(최소 가슴과 양 팔꿈치)이 화면에 들어오게 서기", (255, 255, 0)),
                         ("%.0f초 뒤 시작 (Space = 바로 시작, q = 중단)" % max(left, 0.0), (255, 255, 255)),
                         ("첫 동작: " + STEPS[0][2], (200, 200, 200))]
                if left <= 0.0:
                    script_start = t
            else:
                el = (t - script_start) / 1000.0
                i, left = step_at(el)
                if i >= len(STEPS):
                    state["stop"] = True
                    lines = [("끝", (0, 255, 0))]
                else:
                    nxt = STEPS[i + 1][2] if i + 1 < len(STEPS) else "끝"
                    lines = [("%d/%d  %s" % (i + 1, len(STEPS), STEPS[i][2]), (0, 255, 255)),
                             ("남은 %.1f초   (전체 %.0f / %.0f초)" % (left, el, total), (255, 255, 255)),
                             ("다음: " + nxt, (180, 180, 180))]
            cv2.imshow(WINDOW, _guide(view, lines, font_cache))
            key = cv2.waitKey(1) & 0xFF
            if key == ord(" ") and script_start is None:
                script_start = t
            if key in (ord("q"), 27) or cv2.getWindowProperty(WINDOW, cv2.WND_PROP_VISIBLE) < 1:
                state["stop"] = True
    except KeyboardInterrupt:
        pass
    finally:
        if args.exposure not in (None, "auto"):
            hs.apply_exposure(cap, "auto")
            cap.read()
        cap.release()
        landmarker.close()
        cv2.destroyAllWindows()
        if sock is not None:
            sock.close()

    pose_record.write_csv(os.path.join(out, "joints.csv"), rows)
    write_frames(os.path.join(out, "frames.csv"), frame_rows)
    write_segments(os.path.join(out, "segments.csv"), segments(script_start if script_start is not None else 0))
    found = sum(1 for _t, j in rows if j)
    print("[compare] %d frames, pose in %d, %.1f s -> %s" % (len(rows), found, rows[-1][0] / 1000.0 if rows else 0,
                                                             out), flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
