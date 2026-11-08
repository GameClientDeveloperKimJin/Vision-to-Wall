#!/usr/bin/env python3
"""Pose joints from a PNG sequence or a video file, with the same processing as hand_sensor.py.

Same PoseLandmarker (hand_sensor.create_pose_landmarker: VIDEO mode, 1 pose, confidences 0.5, the same model file),
same landmark reading (hand_sensor.pose_landmarks / pose_world_landmarks) and the same joint conversion
(pose_stream.extract_joints: mirror, Unity axes). Output is the recording CSV of pose_record.py, one row per frame:
13 joints when a pose was found, count 0 otherwise. Differences from the live sensor: every frame runs pose
(no PoseThrottle), no hand landmarker, and frame time = floor(i * 1000 / fps) instead of the camera clock.

Usage:
  python pose_from_media.py FRAMES_DIR_OR_VIDEO [--out detected.csv] [--fps 30]
  python pose_from_media.py --all ../../Temp/pose_check        :: every <clip>/frame_*.png -> <clip>/detected.csv
  python pose_from_media.py ../../Temp/pose_check/wave_right --send   :: also replay to udpPosePort in real time

Options: --config PATH (mirror, udpIp, udpPosePort; default Assets/StreamingAssets/config.json),
--mirror / --no-mirror (override config), --pose-model PATH, --send, --ip, --port.
--send replays like the sensor (count 0 once when the person disappears, then nothing). Do not run it together
with hand_sensor.py on the same port (seq clash).
"""
import argparse
import glob
import math
import os
import socket
import sys
import time

import pose_record
import pose_stream as pst
import sensor_config as scfg

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_CONFIG = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "StreamingAssets", "config.json"))
FRAME_GLOB = "frame_*.png"
DEFAULT_FPS = 30.0


def frame_ms(index, fps):
    """Frame time in ms. Matches PoseTestRenderer.FrameMs (floor of i * 1000 / fps)."""
    return int(math.floor(index * 1000.0 / fps))


def list_frames(directory):
    """Sorted frame_*.png paths in directory (zero-padded names sort by number)."""
    return sorted(glob.glob(os.path.join(directory, FRAME_GLOB)))


def process(images, detect, fps, mirror):
    """images: iterable of RGB frames. detect(rgb, ts_ms) -> (screen, world) as hand_sensor.pose_landmarks /
    pose_world_landmarks (screen None = nobody). Returns [(t_ms, joints)] with joints [] for no pose."""
    rows = []
    last_ts = -1
    for i, rgb in enumerate(images):
        t = frame_ms(i, fps)
        ts = max(t, last_ts + 1)  # VIDEO mode needs strictly increasing timestamps
        last_ts = ts
        screen, world = detect(rgb, ts)
        rows.append((t, pst.extract_joints(screen, world, mirror) if screen is not None else []))
    return rows


def packets(rows):
    """Packets the live sensor would send for rows: a full packet per pose, one count 0 when the person
    disappears, nothing until seen again. Returns [(t_ms, bytes)]."""
    out = []
    seq = 0
    present = False
    for t, joints in rows:
        if not joints:
            if not present:
                continue
            present = False
        else:
            present = True
        seq += 1
        out.append((t, pst.pack_packet(seq, int(time.monotonic() * 1000), joints)))
    return out


def summary(rows):
    found = sum(1 for _t, j in rows if j)
    return "%d frames, pose in %d (%.1f%%)" % (len(rows), found, 100.0 * found / len(rows) if rows else 0.0)


def _read_images(source):
    import cv2  # noqa: E402 (heavy import only when processing media)

    if os.path.isdir(source):
        for path in list_frames(source):
            img = cv2.imread(path)
            if img is None:
                raise IOError("cannot read %s" % path)
            yield cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
        return
    cap = cv2.VideoCapture(source)
    if not cap.isOpened():
        raise IOError("cannot open video %s" % source)
    try:
        while True:
            ok, img = cap.read()
            if not ok:
                break
            yield cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
    finally:
        cap.release()


def _video_fps(source):
    import cv2  # noqa: E402

    cap = cv2.VideoCapture(source)
    fps = cap.get(cv2.CAP_PROP_FPS) if cap.isOpened() else 0.0
    cap.release()
    return fps if fps and fps > 0 else DEFAULT_FPS


def run_one(source, out_path, fps, mirror, pose_model):
    import mediapipe as mp  # noqa: E402
    import hand_sensor as hs  # noqa: E402 (shares create_pose_landmarker and the landmark readers)

    landmarker = hs.create_pose_landmarker(pose_model)

    def detect(rgb, ts):
        result = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)
        screen = hs.pose_landmarks(result)
        world = hs.pose_world_landmarks(result) if screen is not None else None
        return screen, world

    try:
        rows = process(_read_images(source), detect, fps, mirror)
    finally:
        landmarker.close()
    pose_record.write_csv(out_path, rows)
    return rows


def send(rows, ip, port):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    start = time.perf_counter()
    sent = 0
    try:
        for t, data in packets(rows):
            wait = start + t / 1000.0 - time.perf_counter()
            if wait > 0:
                time.sleep(wait)
            sock.sendto(data, (ip, port))
            sent += 1
    finally:
        sock.close()
    return sent


def parse_args(argv):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("source", nargs="?", help="directory with frame_*.png, or a video file")
    p.add_argument("--all", metavar="ROOT", help="process every ROOT/<clip>/ that has frame_*.png")
    p.add_argument("--out", help="output CSV (default: <source dir>/detected.csv)")
    p.add_argument("--fps", type=float, default=None, help="frame rate (PNG default 30, video: from the file)")
    p.add_argument("--config", default=DEFAULT_CONFIG)
    m = p.add_mutually_exclusive_group()
    m.add_argument("--mirror", dest="mirror", action="store_true", default=None)
    m.add_argument("--no-mirror", dest="mirror", action="store_false")
    p.add_argument("--pose-model", default=os.path.join(HERE, "models", "pose_landmarker_lite.task"))
    p.add_argument("--send", action="store_true", help="replay the result to udpPosePort in real time")
    p.add_argument("--ip", default=None)
    p.add_argument("--port", type=int, default=None)
    args = p.parse_args(argv)
    if bool(args.source) == bool(args.all):
        p.error("give either SOURCE or --all ROOT")
    return args


def main(argv=None):
    args = parse_args(sys.argv[1:] if argv is None else argv)
    data, _err = scfg.read_json(args.config)
    cfg, _warnings = scfg.parse_startup(data)
    mirror = cfg["mirror"] if args.mirror is None else args.mirror
    if not os.path.isfile(args.pose_model):
        print("pose model not found: %s (run download_model.py)" % args.pose_model)
        return 2

    if args.all:
        sources = [d for d in sorted(glob.glob(os.path.join(args.all, "*"))) if os.path.isdir(d) and list_frames(d)]
        if not sources:
            print("no <clip>/%s under %s" % (FRAME_GLOB, args.all))
            return 2
    else:
        sources = [args.source]

    all_rows = None
    for source in sources:
        is_dir = os.path.isdir(source)
        fps = args.fps or (DEFAULT_FPS if is_dir else _video_fps(source))
        out = args.out if (args.out and not args.all) else os.path.join(
            source if is_dir else os.path.dirname(os.path.abspath(source)), "detected.csv")
        start = time.perf_counter()
        all_rows = run_one(source, out, fps, mirror, args.pose_model)
        print("%s: %s, %.1fs -> %s" % (os.path.basename(os.path.normpath(source)), summary(all_rows),
                                        time.perf_counter() - start, out), flush=True)

    if args.send:
        if args.all:
            print("--send works with a single SOURCE only")
            return 2
        ip = args.ip or cfg["udpIp"]
        port = args.port or cfg["udpPosePort"]
        if not port:
            print("udpPosePort is 0 (pose stream off) - give --port")
            return 2
        print("sending to %s:%d ..." % (ip, port), flush=True)
        print("sent %d packets" % send(all_rows, ip, port))
    return 0


if __name__ == "__main__":
    sys.exit(main())
