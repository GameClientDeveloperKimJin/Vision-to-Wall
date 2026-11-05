import os
import struct
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import motion_packet as mpk  # noqa: E402


# ---------- pack_packet ----------

def test_packet_length_and_fields():
    pts = [(0.1, 0.2, 0.3), (0.4, 0.5, 0.6)]
    data = mpk.pack_packet(7, 12345, pts)
    assert len(data) == 14 + 12 * 2
    magic, seq, ts, count = struct.unpack_from("<IIIH", data, 0)
    assert magic == 0x57325601
    assert seq == 7
    assert ts == 12345
    assert count == 2
    for i, (x, y, v) in enumerate(pts):
        rx, ry, rv = struct.unpack_from("<fff", data, 14 + 12 * i)
        assert rx == pytest.approx(x)
        assert ry == pytest.approx(y)
        assert rv == pytest.approx(v)


def test_packet_count_zero():
    data = mpk.pack_packet(1, 0, [])
    assert len(data) == 14
    assert struct.unpack("<IIIH", data) == (0x57325601, 1, 0, 0)


def test_packet_little_endian_magic_bytes():
    data = mpk.pack_packet(1, 0, [])
    assert data[:4] == bytes([0x01, 0x56, 0x32, 0x57])


def test_packet_wraps_uint32():
    data = mpk.pack_packet(2 ** 32 + 5, 2 ** 32 + 9, [])
    _, seq, ts, _ = struct.unpack("<IIIH", data)
    assert seq == 5
    assert ts == 9


def test_packet_too_many_points():
    with pytest.raises(ValueError):
        mpk.pack_packet(1, 0, [(0.0, 0.0, 0.0)] * (mpk.MAX_POINTS + 1))


# ---------- to_output_coords ----------

def test_coords_y_inverted_no_mirror():
    assert mpk.to_output_coords(0.2, 0.0, False) == pytest.approx((0.2, 1.0))
    assert mpk.to_output_coords(0.2, 1.0, False) == pytest.approx((0.2, 0.0))
    assert mpk.to_output_coords(0.2, 0.25, False) == pytest.approx((0.2, 0.75))


def test_coords_mirror():
    assert mpk.to_output_coords(0.2, 0.25, True) == pytest.approx((0.8, 0.75))
    assert mpk.to_output_coords(0.0, 0.5, True) == pytest.approx((1.0, 0.5))


@pytest.mark.parametrize("mirror", [False, True])
def test_coords_clamped(mirror):
    for xm, ym in [(-0.3, -0.2), (1.4, 1.7), (-1.0, 2.0), (2.0, -1.0)]:
        x, y = mpk.to_output_coords(xm, ym, mirror)
        assert 0.0 <= x <= 1.0
        assert 0.0 <= y <= 1.0


# ---------- intensity ----------

def test_intensity_scale_and_clamp():
    assert mpk.intensity(0.75, 1.5) == pytest.approx(0.5)
    assert mpk.intensity(0.0, 1.5) == 0.0
    assert mpk.intensity(3.0, 1.5) == 1.0
    assert mpk.intensity(-1.0, 1.5) == 0.0


def test_intensity_zero_ref():
    assert mpk.intensity(0.5, 0.0) == 1.0
    assert mpk.intensity(0.0, 0.0) == 0.0


# ---------- palm / points ----------

def _landmarks():
    # 21 landmarks with distinct coords: i -> (i/100, i/50)
    return [(i / 100.0, i / 50.0) for i in range(21)]


def test_palm_center_is_mean_of_palm_indices():
    lm = _landmarks()
    x, y = mpk.palm_center(lm)
    idx = (0, 5, 9, 13, 17)
    assert x == pytest.approx(sum(i / 100.0 for i in idx) / 5)
    assert y == pytest.approx(sum(i / 50.0 for i in idx) / 5)


def test_hand_points_modes():
    lm = _landmarks()
    assert len(mpk.hand_points(lm, "palm")) == 1
    tips = mpk.hand_points(lm, "tips")
    assert tips == [lm[i] for i in (4, 8, 12, 16, 20)]
    allp = mpk.hand_points(lm, "all")
    assert len(allp) == 6
    assert allp[0] == pytest.approx(mpk.palm_center(lm))
    with pytest.raises(ValueError):
        mpk.hand_points(lm, "nope")


# ---------- match_hands ----------

def test_match_nearest():
    prev = [((0.2, 0.5), "Left"), ((0.8, 0.5), "Right")]
    cur = [((0.78, 0.52), "Right"), ((0.22, 0.49), "Left")]
    assert mpk.match_hands(prev, cur, 0.25, 0.1, 0.75) == [1, 0]


def test_match_new_hand_when_far():
    prev = [((0.1, 0.1), "Left")]
    cur = [((0.9, 0.9), "Left")]
    assert mpk.match_hands(prev, cur, 0.25, 0.1, 0.75) == [None]


def test_match_label_breaks_tie():
    prev = [((0.50, 0.5), "Left"), ((0.52, 0.5), "Right")]
    cur = [((0.51, 0.5), "Right")]
    assert mpk.match_hands(prev, cur, 0.25, 0.1, 0.75) == [1]


def test_match_one_to_one():
    prev = [((0.5, 0.5), "Left")]
    cur = [((0.5, 0.5), "Left"), ((0.52, 0.5), "Left")]
    assert mpk.match_hands(prev, cur, 0.25, 0.1, 0.75) == [0, None]


def test_normalized_distance_aspect():
    # dy of 0.4 in a 4:3 frame (aspect 0.75) = 0.3 screen widths
    assert mpk.normalized_distance((0.0, 0.0), (0.0, 0.4), 0.75) == pytest.approx(0.3)
