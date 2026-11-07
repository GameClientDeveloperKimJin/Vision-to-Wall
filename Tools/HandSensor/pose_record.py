#!/usr/bin/env python3
"""Pose joint recording (what was sent on udpPosePort) and the arm-raise check used for R2 human verification.

hand_sensor.py --preview: Enter starts / stops a recording -> recordings/pose_YYYYMMDD_HHMMSS.csv
(joint numbers only, no camera image). On stop the check below is printed.

Check a recording again:
  python pose_record.py recordings/pose_20261108_150000.csv

Check: a wrist counts as raised when its visibility >= 0.5 and its screen y is above the nose.
  right arm: raised >= 1 s, screen x on the right half while raised (mirror), world y higher than when lowered
  left arm:  the same on the left half
Standard library only (+ pose_stream.py next to this file).
"""
import csv
import os
import sys
import time

import pose_stream as ps

FIELDS = ("sx", "sy", "wx", "wy", "wz", "v")
VISIBLE = 0.5
MIN_RAISED_SEC = 1.0
NOSE, LEFT_WRIST, RIGHT_WRIST = 0, 5, 6


def header():
    cols = ["t_ms", "count"]
    for name in ps.JOINT_NAMES:
        cols.extend("%s_%s" % (name, f) for f in FIELDS)
    return cols


class PoseRecorder:
    """Keeps rows in memory while recording (no file I/O in the frame loop); save() writes the CSV."""

    def __init__(self):
        self.rows = None
        self._t0 = 0

    @property
    def active(self):
        return self.rows is not None

    def start(self, t_ms):
        self.rows = []
        self._t0 = t_ms

    def add(self, t_ms, joints):
        if self.rows is not None:
            self.rows.append((t_ms - self._t0, joints))

    def stop(self, directory):
        """Writes the recording and returns (path, rows). rows = [(t_ms, joints)]."""
        rows, self.rows = self.rows or [], None
        os.makedirs(directory, exist_ok=True)
        path = os.path.join(directory, time.strftime("pose_%Y%m%d_%H%M%S.csv"))
        write_csv(path, rows)
        return path, rows


def write_csv(path, rows):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header())
        for t_ms, joints in rows:
            line = [t_ms, len(joints)]
            for j in joints:
                line.extend("%.4f" % v for v in j)
            w.writerow(line)


def read_csv(path):
    rows = []
    with open(path, newline="", encoding="utf-8") as f:
        r = csv.reader(f)
        next(r)
        for line in r:
            count = int(line[1])
            values = [float(v) for v in line[2:2 + count * len(FIELDS)]]
            joints = [tuple(values[i * 6:i * 6 + 6]) for i in range(count)]
            rows.append((int(line[0]), joints))
    return rows


def _frame_sec(rows):
    if len(rows) < 2:
        return 0.0
    return (rows[-1][0] - rows[0][0]) / 1000.0 / (len(rows) - 1)


def check_arm(rows, wrist, right):
    """Returns dict with raised_sec, mean_x, wy_up, wy_down, ok, reason."""
    up, down = [], []
    for _t, joints in rows:
        if not joints:
            continue
        w, nose = joints[wrist], joints[NOSE]
        if w[5] >= VISIBLE and w[1] > nose[1]:
            up.append(w)
        else:
            down.append(w)
    dt = _frame_sec(rows)
    res = {"raised_sec": len(up) * dt, "mean_x": None, "wy_up": None, "wy_down": None}
    if up:
        res["mean_x"] = sum(w[0] for w in up) / len(up)
        res["wy_up"] = sum(w[3] for w in up) / len(up)
    if down:
        res["wy_down"] = sum(w[3] for w in down) / len(down)
    if res["raised_sec"] < MIN_RAISED_SEC:
        res["ok"], res["reason"] = False, "raised %.1fs < %.0fs" % (res["raised_sec"], MIN_RAISED_SEC)
    elif (res["mean_x"] > 0.5) != right:
        res["ok"], res["reason"] = False, "screen x %.2f on the wrong half" % res["mean_x"]
    elif res["wy_down"] is not None and res["wy_up"] <= res["wy_down"]:
        res["ok"], res["reason"] = False, "world y did not rise"
    else:
        res["ok"], res["reason"] = True, ""
    return res


def report(rows):
    """Returns (all ok, text lines)."""
    people = sum(1 for _t, j in rows if j)
    dur = (rows[-1][0] - rows[0][0]) / 1000.0 if len(rows) > 1 else 0.0
    lines = ["recording: %.1fs, %d packets (%d with a person, %d count 0)"
             % (dur, len(rows), people, len(rows) - people)]
    ok_all = people > 0
    for label, wrist, right in (("right arm", RIGHT_WRIST, True), ("left arm", LEFT_WRIST, False)):
        r = check_arm(rows, wrist, right)
        ok_all = ok_all and r["ok"]
        detail = "raised %.1fs" % r["raised_sec"]
        if r["mean_x"] is not None:
            detail += ", screen x %.2f (%s half)" % (r["mean_x"], "right" if r["mean_x"] > 0.5 else "left")
            detail += ", world y up %+.2f" % r["wy_up"]
            if r["wy_down"] is not None:
                detail += " / down %+.2f" % r["wy_down"]
        lines.append("%s: %s  %s%s" % (label, "PASS" if r["ok"] else "FAIL", detail,
                                       "" if r["ok"] else "  (" + r["reason"] + ")"))
    lines.append("result: %s" % ("PASS" if ok_all else "FAIL"))
    return ok_all, lines


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) != 1:
        print(__doc__)
        return 2
    rows = read_csv(argv[0])
    ok, lines = report(rows)
    for line in lines:
        print(line)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
