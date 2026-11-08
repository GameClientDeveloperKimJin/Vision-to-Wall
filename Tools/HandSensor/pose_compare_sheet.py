#!/usr/bin/env python3
"""Side-by-side images and the mismatch list for a comparison take (R4b, doc 57).

Input: a take folder from pose_compare_record.py after Unity's VisionToWall/Avatar/Compare Latest Recording
(avatar.csv: avatar bone points seen from behind + per-limb metrics, see AvatarCompare.cs).

Output in the take folder:
  sheet/NNNNN.jpg    [camera (mirrored, as on the wall) with the detected joints | avatar from behind]
  mismatch.md        per step and limb: angle median / 90th pct / share over 25 deg, share at the image edge
                     (framing: no valid 2D reference, not compared), cause hints; the runs where a limb stays over
                     25 deg for >= 0.2 s

Usage:
  python pose_compare_sheet.py recordings/compare_x [--avatar avatar.csv] [--every 3] [--frames 120-300]
Standard library + OpenCV / numpy (already in the sensor venv).
"""
import argparse
import csv
import math
import os
import sys

LIMBS = ("LUpperArm", "LForeArm", "RUpperArm", "RForeArm", "LThigh", "LShin", "RThigh", "RShin")
POINTS = ("Head", "LShoulder", "RShoulder", "LElbow", "RElbow", "LWrist", "RWrist",
          "LHip", "RHip", "LKnee", "RKnee", "LAnkle", "RAnkle")
# avatar panel bones (indices into POINTS)
BONES = ((1, 2), (1, 3), (3, 5), (2, 4), (4, 6), (7, 8), (1, 7), (2, 8), (7, 9), (9, 11), (8, 10), (10, 12), (0, 1),
         (0, 2))
# camera joints (packet order: Nose, LS, RS, LE, RE, LW, RW, LH, RH, LK, RK, LA, RA)
CAM_BONES = ((1, 2), (1, 3), (3, 5), (2, 4), (4, 6), (7, 8), (1, 7), (2, 8), (7, 9), (9, 11), (8, 10), (10, 12))
OVER_DEG = 25.0
MIN_RUN_SEC = 0.2


def read_rows(path):
    with open(path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def num(v):
    return float(v) if v not in ("", None) else float("nan")


def median(values):
    v = sorted(x for x in values if not math.isnan(x))
    return v[len(v) // 2] if v else float("nan")


def percentile(values, p):
    v = sorted(x for x in values if not math.isnan(x))
    return v[min(len(v) - 1, int(round(p * (len(v) - 1))))] if v else float("nan")


def runs(rows, limb, over=OVER_DEG, min_sec=MIN_RUN_SEC):
    """[(start_ms, end_ms, median angle)] where the limb's angle stays above `over` for at least min_sec."""
    out, start, angles, last = [], None, [], None
    for r in rows:
        a = num(r[limb + "_angle"])
        t = int(r["t_ms"])
        if not math.isnan(a) and a > over:
            if start is None:
                start, angles = t, []
            angles.append(a)
            last = t
        elif start is not None and (math.isnan(a) or a <= over):
            if (last - start) / 1000.0 >= min_sec:
                out.append((start, last, median(angles)))
            start = None
    if start is not None and (last - start) / 1000.0 >= min_sec:
        out.append((start, last, median(angles)))
    return out


def cause(rows, limb, start, end):
    """Hint from the cause columns over a run."""
    sel = [r for r in rows if start <= int(r["t_ms"]) <= end]
    ang = median(num(r[limb + "_angle"]) for r in sel)
    world = median(num(r[limb + "_world"]) for r in sel)
    lag = median(num(r[limb + "_lag"]) for r in sel)
    swap = median(num(r[limb + "_swap"]) for r in sel)
    if not math.isnan(swap) and swap < 15.0:
        return "좌우 (반대쪽 팔다리와 일치 %.0f°)" % swap
    if not math.isnan(world) and world > OVER_DEG:
        return "인식 (원본 world와 화면 2D 차 %.0f°)" % world
    if not math.isnan(lag) and lag < 15.0 <= ang:
        return "평활 지연 (0.2초 전 화면과 %.0f°)" % lag
    return "아바타 처리 (world %.0f°, 지연 보정 %.0f°)" % (world, lag)


def report(take, rows, segs):
    lines = ["| 구간 | 팔다리 | 비교 프레임 | 각도 중앙값 | 90% | 25° 초과 | 원본 world 차 | 가시성 | 화면 경계 | 추적 중 | 깊이(정면 길이 차) |",
             "|---|---|---|---|---|---|---|---|---|---|---|"]
    for s in segs:
        a, b = int(s["start_ms"]), int(s["end_ms"])
        sel = [r for r in rows if a <= int(r["t_ms"]) < b]
        if not sel:
            continue
        for limb in LIMBS:
            vis = sum(1 for r in sel if r[limb + "_vis"] == "1") / len(sel)
            tracked = sum(1 for r in sel if num(r[limb + "_w"]) >= 0.99) / len(sel)
            off = sum(1 for r in sel if r[limb + "_vis"] == "1" and r.get(limb + "_off") == "1") / len(sel)
            ang = [num(r[limb + "_angle"]) for r in sel]
            n = sum(1 for x in ang if not math.isnan(x))
            if n == 0 and vis == 0:
                continue
            over = sum(1 for x in ang if not math.isnan(x) and x > OVER_DEG) / n if n else float("nan")
            depth = [num(r[limb + "_avatarFrontal"]) - num(r[limb + "_camRatio"]) for r in sel
                     if r[limb + "_short"] == "1" and r[limb + "_vis"] == "1" and num(r[limb + "_w"]) >= 0.99]
            lines.append("| %s | %s | %d | %.1f | %.1f | %.0f%% | %.1f | %.0f%% | %.0f%% | %.0f%% | %s |" % (
                s["id"], limb, n, median(ang), percentile(ang, 0.9), over * 100 if n else float("nan"),
                median(num(r[limb + "_world"]) for r in sel if not math.isnan(num(r[limb + "_angle"]))),
                vis * 100, off * 100, tracked * 100, "%.2f" % median(depth) if depth else "-"))
    lines += ["", "어긋남 구간 (각도 %.0f° 초과 %.1f초 이상)" % (OVER_DEG, MIN_RUN_SEC), "",
              "| 시작 | 끝 | 구간 | 팔다리 | 각도 | 원인 힌트 |", "|---|---|---|---|---|---|"]
    for limb in LIMBS:
        for start, end, ang in runs(rows, limb):
            step = next((s["id"] for s in segs if int(s["start_ms"]) <= start < int(s["end_ms"])), "-")
            lines.append("| %.2f | %.2f | %s | %s | %.0f | %s |" % (start / 1000.0, end / 1000.0, step, limb, ang,
                                                                 cause(rows, limb, start, end)))
    path = os.path.join(take, "mismatch.md")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return path


def sheets(take, avatar_rows, every, frame_range):
    import cv2
    import numpy as np

    frames = {int(r["t_ms"]): int(r["frame"]) for r in read_rows(os.path.join(take, "frames.csv"))}
    joints = {int(r["t_ms"]): r for r in read_rows(os.path.join(take, "joints.csv"))}
    out = os.path.join(take, "sheet")
    os.makedirs(out, exist_ok=True)
    names = ("Nose", "LeftShoulder", "RightShoulder", "LeftElbow", "RightElbow", "LeftWrist", "RightWrist",
             "LeftHip", "RightHip", "LeftKnee", "RightKnee", "LeftAnkle", "RightAnkle")
    written = 0
    for k, r in enumerate(avatar_rows):
        t = int(r["t_ms"])
        index = frames.get(t)
        if index is None or k % every:
            continue
        if frame_range and not frame_range[0] <= index <= frame_range[1]:
            continue
        img = cv2.imread(os.path.join(take, "frames", "%05d.jpg" % index))
        if img is None:
            continue
        img = cv2.flip(img, 1)  # as on the wall (mirror)
        h, w = img.shape[:2]
        j = joints.get(t)
        if j and j["count"] != "0":
            pts = [(int(num(j[n + "_sx"]) * w), int((1.0 - num(j[n + "_sy"])) * h), num(j[n + "_v"])) for n in names]
            for a, b in CAM_BONES:
                if pts[a][2] >= 0.5 and pts[b][2] >= 0.5:
                    cv2.line(img, pts[a][:2], pts[b][:2], (0, 255, 0), 2)
            for x, y, v in pts:
                cv2.circle(img, (x, y), 4, (0, 255, 0) if v >= 0.5 else (0, 0, 255), -1)
        panel = np.zeros((h, w, 3), np.uint8)
        av = [(num(r[p + "_x"]), num(r[p + "_y"])) for p in POINTS]
        scale = h * 0.8  # avatar is 1 unit tall
        cx, cy = w // 2, int(h * 0.95)
        ap = [(int(cx + x * scale), int(cy - (y + 0.5) * scale)) for x, y in av]
        for a, b in BONES:
            cv2.line(panel, ap[a], ap[b], (255, 200, 80), 3)
        for i, limb in enumerate(LIMBS):
            ang = num(r[limb + "_angle"])
            color = (0, 0, 255) if not math.isnan(ang) and ang > OVER_DEG else (180, 180, 180)
            txt = "%s %s" % (limb, "-" if math.isnan(ang) else "%.0f" % ang)
            cv2.putText(panel, txt, (8, 20 + 18 * i), cv2.FONT_HERSHEY_SIMPLEX, 0.5, color, 1)
        cv2.putText(img, "camera (mirrored) t=%.2fs" % (t / 1000.0), (8, h - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5,
                    (255, 255, 255), 1)
        cv2.putText(panel, "avatar from behind", (8, h - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        cv2.imwrite(os.path.join(out, "%05d.jpg" % index), np.hstack([img, panel]))
        written += 1
    return written


def parse_args(argv):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("take")
    p.add_argument("--avatar", default="avatar.csv")
    p.add_argument("--every", type=int, default=3, help="one sheet every N packets (0 = no sheets)")
    p.add_argument("--frames", default=None, help="frame index range a-b")
    return p.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    rows = read_rows(os.path.join(args.take, args.avatar))
    segs = read_rows(os.path.join(args.take, "segments.csv"))
    print("[sheet] %s" % report(args.take, rows, segs))
    if args.every > 0:
        fr = tuple(int(x) for x in args.frames.split("-")) if args.frames else None
        print("[sheet] %d images -> %s" % (sheets(args.take, rows, args.every, fr), os.path.join(args.take, "sheet")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
