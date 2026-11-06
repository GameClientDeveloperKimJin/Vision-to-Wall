import os
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import body_tracker as bt  # noqa: E402

ASPECT = 0.75
HIDDEN = (0.0, 0.0, 0.0)


def pose(**points):
    """33 hidden landmarks, overriding by name: ls, rs, lh, rh = (x, y, visibility)."""
    lms = [HIDDEN] * 33
    index = {"ls": bt.LEFT_SHOULDER, "rs": bt.RIGHT_SHOULDER, "lh": bt.LEFT_HIP, "rh": bt.RIGHT_HIP}
    for name, value in points.items():
        lms[index[name]] = value
    return lms


# ---------- body_center ----------

def test_shoulders_only():
    assert bt.body_center(pose(ls=(0.4, 0.3, 0.9), rs=(0.6, 0.5, 0.9))) == pytest.approx((0.5, 0.4))


def test_shoulders_and_hips():
    c = bt.body_center(pose(ls=(0.4, 0.2, 0.9), rs=(0.6, 0.2, 0.9), lh=(0.4, 0.6, 0.9), rh=(0.6, 0.6, 0.9)))
    assert c == pytest.approx((0.5, 0.4))


def test_one_shoulder_and_one_hip():
    c = bt.body_center(pose(ls=(0.4, 0.2, 0.9), rs=(0.9, 0.9, 0.1), lh=(0.6, 0.6, 0.9), rh=(0.9, 0.9, 0.1)))
    assert c == pytest.approx((0.5, 0.4))


def test_no_visible_shoulder_is_nobody():
    assert bt.body_center(pose(ls=(0.4, 0.2, 0.49), rs=(0.6, 0.2, 0.1), lh=(0.4, 0.6, 0.9), rh=(0.6, 0.6, 0.9))) is None


def test_visibility_threshold_is_inclusive():
    assert bt.body_center(pose(ls=(0.4, 0.2, bt.VISIBILITY_MIN))) == pytest.approx((0.4, 0.2))


def test_missing_visibility_counts_as_hidden():
    assert bt.body_center(pose(ls=(0.4, 0.2, None))) is None


# ---------- BodyTracker ----------

def shoulders_at(x, y=0.4):
    return pose(ls=(x, y, 0.9), rs=(x, y, 0.9))


def test_mirror_and_y_flip():
    point, center = bt.BodyTracker(True).update(shoulders_at(0.2, 0.3), 0.0, ASPECT)
    assert point[:2] == pytest.approx((0.8, 0.7))
    assert center == pytest.approx((0.2, 0.3))


def test_no_mirror():
    point, _ = bt.BodyTracker(False).update(shoulders_at(0.2, 0.3), 0.0, ASPECT)
    assert point[:2] == pytest.approx((0.2, 0.7))


def test_coords_are_clamped():
    point, _ = bt.BodyTracker(False).update(shoulders_at(1.2, -0.3), 0.0, ASPECT)
    assert point[:2] == pytest.approx((1.0, 1.0))


def test_first_frame_has_zero_speed():
    point, _ = bt.BodyTracker(False).update(shoulders_at(0.5), 0.0, ASPECT)
    assert point[2] == 0.0


def test_speed_is_smoothed_and_normalized():
    tr = bt.BodyTracker(False)
    tr.update(shoulders_at(0.5), 0.0, ASPECT)
    point, _ = tr.update(shoulders_at(0.6), 0.1, ASPECT)  # raw 1.0 w/s, EMA 0.5
    assert point[2] == pytest.approx(0.5 / bt.SPEED_REF)


def test_nobody_returns_none_and_resets_speed():
    tr = bt.BodyTracker(False)
    tr.update(shoulders_at(0.5), 0.0, ASPECT)
    tr.update(shoulders_at(0.6), 0.1, ASPECT)
    assert tr.update(None, 0.2, ASPECT) == (None, None)
    point, _ = tr.update(shoulders_at(0.9), 0.3, ASPECT)
    assert point[2] == 0.0  # no jump in speed when the person comes back


def test_hidden_shoulders_count_as_nobody():
    assert bt.BodyTracker(False).update(pose(), 0.0, ASPECT) == (None, None)


# ---------- PoseThrottle ----------

def test_throttle_runs_every_frame_by_default():
    th = bt.PoseThrottle()
    assert all(th.should_run() for _ in range(5))


def feed(th, ms, count=bt.THROTTLE_WINDOW):
    for _ in range(count):
        th.add_inference(ms)


def test_throttle_runs_every_other_frame_when_slow():
    th = bt.PoseThrottle()
    feed(th, 50.0)
    assert th.active is True
    assert [th.should_run() for _ in range(4)] == [True, False, True, False]


def test_throttle_stays_off_with_fast_inference():
    """Measured case: camera limited to 15 fps, inference about 17 ms -> pose keeps running every frame."""
    th = bt.PoseThrottle()
    feed(th, 17.0)
    assert th.active is False
    assert th.average_ms == pytest.approx(17.0)


def test_throttle_waits_for_full_window():
    th = bt.PoseThrottle()
    feed(th, 500.0, bt.THROTTLE_WINDOW - 1)
    assert th.active is False
    th.add_inference(500.0)
    assert th.active is True


def test_throttle_ignores_single_warmup_spike():
    th = bt.PoseThrottle()
    th.add_inference(500.0)
    feed(th, 17.0, bt.THROTTLE_WINDOW - 1)
    assert th.active is False


def test_throttle_hysteresis():
    th = bt.PoseThrottle()
    feed(th, bt.THROTTLE_ENTER_MS)  # the limit itself does not enter
    assert th.active is False
    feed(th, 50.0)
    assert th.active is True
    feed(th, 35.0)  # inside the gap: stays throttled
    assert th.active is True
    feed(th, bt.THROTTLE_EXIT_MS)  # the limit itself does not exit
    assert th.active is True
    feed(th, 29.0)
    assert th.active is False
