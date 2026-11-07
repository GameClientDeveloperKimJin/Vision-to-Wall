import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import fake_pose_sender as fps  # noqa: E402
import pose_record as pr  # noqa: E402


def fake_rows(pattern, seconds, hz=30):
    return [(int(i * 1000 / hz), fps.to_joints(fps.pose_world(pattern, i / float(hz))))
            for i in range(int(seconds * hz))]


def swap_sides(joints):
    """Simulates a left/right label bug: left and right joints exchanged."""
    out = list(joints)
    for a in range(1, 13, 2):
        out[a], out[a + 1] = joints[a + 1], joints[a]
    return out


def test_raise_pattern_passes():
    ok, lines = pr.report(fake_rows("raise", 8))
    assert ok, lines
    assert lines[-1] == "result: PASS"


def test_no_raise_fails():
    rows = [(t, j) for t, j in fake_rows("raise", 8) if (t // 2000) % 2 == 0]  # right arm only
    ok, lines = pr.report(rows)
    assert not ok
    assert lines[1].startswith("right arm: PASS") and lines[2].startswith("left arm: FAIL")


def test_swapped_labels_fail():
    rows = [(t, swap_sides(j)) for t, j in fake_rows("raise", 8)]
    ok, lines = pr.report(rows)
    assert not ok
    assert "wrong half" in lines[1] and "wrong half" in lines[2]


def test_low_visibility_wrist_is_not_raised():
    rows = []
    for t, j in fake_rows("raise", 8):
        j = list(j)
        j[6] = j[6][:5] + (0.2,)
        rows.append((t, j))
    r = pr.check_arm(rows, pr.RIGHT_WRIST, True)
    assert r["raised_sec"] == 0.0 and not r["ok"]


def test_recorder_csv_round_trip(tmp_path):
    rec = pr.PoseRecorder()
    assert not rec.active
    rec.add(0, [])  # ignored while not recording
    rec.start(5000)
    src = fake_rows("wave", 1, hz=10)
    for t, j in src:
        rec.add(5000 + t, j)
    rec.add(6000, [])
    path, rows = rec.stop(str(tmp_path))
    assert not rec.active
    back = pr.read_csv(path)
    assert len(back) == len(src) + 1 and back[-1] == (1000, [])
    assert back[0][0] == 0 and len(back[0][1]) == 13
    assert all(abs(a - b) < 1e-3 for a, b in zip(back[3][1][6], src[3][1][6]))


def test_empty_recording_fails():
    ok, lines = pr.report([])
    assert not ok and lines[-1] == "result: FAIL"
