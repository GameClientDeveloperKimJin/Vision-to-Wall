import os
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import hand_tracker as ht  # noqa: E402

ASPECT = 0.75
DT = 1.0 / 15.0  # 15 fps camera
TUNING = {"handSpeedRef": 1.5, "handMinSpeed": 0.05, "handEmitStill": False}


def hand_at(x, y, label="Right"):
    """21 landmarks all at (x, y) -> palm center (x, y)."""
    return ([(x, y)] * 21, label)


def make(tuning=None, mode="palm"):
    return ht.HandTracker(mode, False, dict(tuning or TUNING))


def test_new_hand_not_sent_first_frame():
    tr = make()
    pts, _ = tr.update([hand_at(0.5, 0.5)], 0.0, ASPECT)
    assert pts == []


def test_moving_hand_sent_with_intensity():
    tr = make()
    tr.update([hand_at(0.5, 0.5)], 0.0, ASPECT)
    pts, _ = tr.update([hand_at(0.55, 0.5)], DT, ASPECT)  # raw 0.75 w/s, EMA 0.375
    assert len(pts) == 1
    x, y, v = pts[0]
    assert x == pytest.approx(0.55) and y == pytest.approx(0.5)
    assert v == pytest.approx(0.375 / 1.5)


def test_still_hand_never_sent():
    tr = make()
    for i in range(20):
        pts, _ = tr.update([hand_at(0.5, 0.5)], i * DT, ASPECT)
        assert pts == []


def test_wave_turnaround_is_held():
    """Speed drops below minSpeed at the turn of a wave -> still sent during HOLD_SEC."""
    tr = make()
    t = 0.0
    tr.update([hand_at(0.40, 0.5)], t, ASPECT)
    for x in (0.45, 0.50, 0.55):
        t += DT
        pts, _ = tr.update([hand_at(x, 0.5)], t, ASPECT)
        assert len(pts) == 1
    peak = pts[0][2]
    # hand pauses at the turn for 2 frames (well inside HOLD_SEC = 0.3 s)
    for _ in range(2):
        t += DT
        pts, _ = tr.update([hand_at(0.55, 0.5)], t, ASPECT)
        assert len(pts) == 1
        assert 0.0 < pts[0][2] <= peak
    assert tr.held_frames == 2


def test_hold_expires():
    tr = make()
    tr.update([hand_at(0.4, 0.5)], 0.0, ASPECT)
    tr.update([hand_at(0.5, 0.5)], DT, ASPECT)
    t = DT
    sent = []
    for _ in range(15):  # 1 s of stillness
        t += DT
        pts, _ = tr.update([hand_at(0.5, 0.5)], t, ASPECT)
        sent.append(len(pts))
    assert sent[0] == 1          # held right after moving
    assert sent[-1] == 0         # released
    # released within ~0.6 s (smoothed speed decay + HOLD_SEC), then stays released
    last = max(i for i, n in enumerate(sent) if n)
    assert last * DT <= 0.6
    assert all(n == 0 for n in sent[last + 1:])


def test_hold_fades():
    tr = make()
    tr.update([hand_at(0.4, 0.5)], 0.0, ASPECT)
    pts, _ = tr.update([hand_at(0.5, 0.5)], DT, ASPECT)
    peak = pts[0][2]
    t = DT
    vs = []
    for _ in range(3):
        t += DT
        pts, _ = tr.update([hand_at(0.5, 0.5)], t, ASPECT)
        if pts:
            vs.append(pts[0][2])
    assert vs == sorted(vs, reverse=True)
    assert vs[-1] >= peak * ht.HOLD_MIN_FACTOR - 1e-9


def test_lost_hand_sent_at_last_position_then_resumes():
    """Detector misses the hand for 2 frames (motion blur) -> keep sending, then same hand continues."""
    tr = make()
    tr.update([hand_at(0.40, 0.5)], 0.0, ASPECT)
    pts, _ = tr.update([hand_at(0.50, 0.5)], DT, ASPECT)
    assert len(pts) == 1
    for k in (2, 3):
        pts, _ = tr.update([], k * DT, ASPECT)
        assert len(pts) == 1
        assert pts[0][0] == pytest.approx(0.50)
    pts, _ = tr.update([hand_at(0.70, 0.5)], 4 * DT, ASPECT)
    assert len(pts) == 1  # matched to the lost track, not a new hand
    assert len(tr.tracks) == 1


def test_lost_hand_dropped_after_grace():
    tr = make()
    tr.update([hand_at(0.4, 0.5)], 0.0, ASPECT)
    tr.update([hand_at(0.5, 0.5)], DT, ASPECT)
    pts, _ = tr.update([], DT + ht.TRACK_GRACE_SEC + 0.01, ASPECT)
    assert pts == []
    assert tr.tracks == []


def test_emit_still():
    tr = make({"handSpeedRef": 1.5, "handMinSpeed": 0.05, "handEmitStill": True})
    tr.update([hand_at(0.5, 0.5)], 0.0, ASPECT)
    pts, prev = tr.update([hand_at(0.5, 0.5)], DT, ASPECT)
    assert len(pts) == 1 and pts[0][2] == pytest.approx(ht.STILL_INTENSITY)
    assert prev[0][3] is True


def test_tips_mode_point_count_and_tuning_update():
    tr = make(mode="all")
    tr.update([hand_at(0.4, 0.5)], 0.0, ASPECT)
    pts, _ = tr.update([hand_at(0.5, 0.5)], DT, ASPECT)
    assert len(pts) == 6
    tr.apply_tuning({"handSpeedRef": 1.5, "handMinSpeed": 100.0, "handEmitStill": False})
    tr.reset()
    tr.update([hand_at(0.4, 0.5)], 0.0, ASPECT)
    pts, _ = tr.update([hand_at(0.5, 0.5)], DT, ASPECT)
    assert pts == []


def test_two_hands_independent():
    tr = make()
    tr.update([hand_at(0.2, 0.5, "Left"), hand_at(0.8, 0.5, "Right")], 0.0, ASPECT)
    pts, _ = tr.update([hand_at(0.2, 0.5, "Left"), hand_at(0.9, 0.5, "Right")], DT, ASPECT)
    assert len(pts) == 1 and pts[0][0] == pytest.approx(0.9)
