import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import pose_compare_record as pcr  # noqa: E402
import pose_compare_sheet as pcs  # noqa: E402


def test_script_steps_and_total():
    assert pcr.total_sec() == sum(s[0] for s in pcr.STEPS)
    assert pcr.step_at(0.0) == (0, pcr.STEPS[0][0])
    i, left = pcr.step_at(pcr.STEPS[0][0] + 0.5)
    assert i == 1 and abs(left - (pcr.STEPS[1][0] - 0.5)) < 1e-9
    assert pcr.step_at(pcr.total_sec() + 1.0) == (len(pcr.STEPS), 0.0)


def test_segments_follow_the_script_on_the_recording_clock(tmp_path):
    segs = pcr.segments(1500)
    assert segs[0][3] == 1500
    assert segs[0][4] == segs[1][3]
    assert segs[-1][4] == 1500 + int(pcr.total_sec() * 1000)
    path = tmp_path / "segments.csv"
    pcr.write_segments(str(path), segs)
    rows = list(csv.DictReader(open(path, encoding="utf-8")))
    assert [r["id"] for r in rows] == [s[1] for s in pcr.STEPS]


def _row(t, angle):
    r = {"t_ms": str(t)}
    for limb in pcs.LIMBS:
        r[limb + "_angle"] = ""
    r["RUpperArm_angle"] = "" if angle is None else str(angle)
    return r


def test_runs_need_the_minimum_duration():
    rows = [_row(t, a) for t, a in [(0, 10), (33, 30), (66, 40), (100, 35), (133, 30), (166, 32), (200, 31),
                                    (233, 33), (266, 10), (300, 30), (333, 10)]]
    runs = pcs.runs(rows, "RUpperArm")
    assert len(runs) == 1  # 33..233 ms is 0.2 s; the single frame at 300 ms is not a run
    start, end, med = runs[0]
    assert (start, end) == (33, 233) and 30 <= med <= 40


def test_runs_break_on_missing_frames():
    rows = [_row(0, 30), _row(100, 30), _row(200, None), _row(300, 30), _row(400, 30)]
    assert pcs.runs(rows, "RUpperArm", min_sec=0.05) == [(0, 100, 30.0), (300, 400, 30.0)]
