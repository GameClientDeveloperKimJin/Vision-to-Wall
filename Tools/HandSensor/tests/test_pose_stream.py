import os
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import pose_stream as ps  # noqa: E402

RIGHT_WRIST_MP = 16
LEFT_WRIST_MP = 15


def screen_lms(overrides=None):
    """33 MediaPipe image landmarks (x, y, visibility); default all at the center."""
    lms = [(0.5, 0.5, 0.9)] * 33
    for i, v in (overrides or {}).items():
        lms[i] = v
    return lms


def world_lms(overrides=None):
    lms = [(0.0, 0.0, 0.0)] * 33
    for i, v in (overrides or {}).items():
        lms[i] = v
    return lms


# ---------- protocol constants (must match Core/PoseProtocol.cs) ----------

def test_constants_match_core_protocol():
    assert ps.MAGIC == 0x57325602
    assert ps.HEADER.size == 14
    assert ps.JOINT.size == 24
    assert ps.JOINT_COUNT == 13
    assert ps.PACKET_SIZE == 326
    assert ps.EMPTY_PACKET_SIZE == 14
    assert ps.MEDIAPIPE_INDEX == (0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28)
    assert ps.JOINT_NAMES[6] == "RightWrist" and ps.MEDIAPIPE_INDEX[6] == RIGHT_WRIST_MP
    assert ps.JOINT_NAMES[5] == "LeftWrist" and ps.MEDIAPIPE_INDEX[5] == LEFT_WRIST_MP


def test_core_protocol_file_agrees():
    """Reads Core/PoseProtocol.cs so a contract change there fails here."""
    here = os.path.dirname(os.path.abspath(__file__))
    path = os.path.join(here, "..", "..", "..", "Assets", "02_Scripts", "Core", "PoseProtocol.cs")
    if not os.path.isfile(path):
        pytest.skip("Unity project not next to Tools/")
    text = open(path, encoding="utf-8-sig").read()
    assert "0x57325602" in text
    assert "{ 0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28 }" in text


# ---------- fixed bytes ----------

def test_empty_packet_bytes():
    data = ps.pack_packet(1, 1000, [])
    assert data == bytes.fromhex("02563257" "01000000" "e8030000" "0000")


def test_full_packet_bytes():
    joints = [(0.5, 1.0, -1.0, 2.0, 0.25, 1.0)] + [(0.0,) * 6] * 12
    data = ps.pack_packet(0x01020304, 0xFFFFFFFF + 6, joints)  # timestamp wraps to 5
    assert len(data) == 326
    assert data[:14] == bytes.fromhex("02563257" "04030201" "05000000" "0d00")
    assert data[14:38] == bytes.fromhex("0000003f" "0000803f" "000080bf" "00000040" "0000803e" "0000803f")
    assert data[38:] == bytes(24 * 12)


def test_round_trip():
    joints = ps.extract_joints(screen_lms({0: (0.3, 0.2, 0.7)}), world_lms({0: (0.1, -0.6, -0.2)}), True)
    seq, ts, back = ps.unpack_packet(ps.pack_packet(7, 1234, joints))
    assert (seq, ts) == (7, 1234)
    assert back == [pytest.approx(j) for j in joints]


def test_pack_rejects_wrong_count():
    with pytest.raises(ValueError):
        ps.pack_packet(1, 0, [(0.0,) * 6] * 5)


@pytest.mark.parametrize("data", [
    b"\x00" * 13,
    bytes.fromhex("01563257" "01000000" "00000000" "0000"),          # hand stream magic
    bytes.fromhex("02563257" "01000000" "00000000" "0d00"),          # count 13, no body
    bytes.fromhex("02563257" "01000000" "00000000" "0500") + bytes(120),
])
def test_unpack_rejects_bad_packets(data):
    with pytest.raises(ValueError):
        ps.unpack_packet(data)


# ---------- coordinates ----------

def test_screen_coords_flip_y_and_mirror_x():
    j = ps.extract_joints(screen_lms({0: (0.2, 0.1, 0.8)}), None, True)[0]
    assert j[:2] == pytest.approx((0.8, 0.9))
    j = ps.extract_joints(screen_lms({0: (0.2, 0.1, 0.8)}), None, False)[0]
    assert j[:2] == pytest.approx((0.2, 0.9))


def test_screen_coords_clamped():
    j = ps.extract_joints(screen_lms({27: (-0.2, 1.3, 0.1)}), None, False)[11]
    assert j[:2] == (0.0, 0.0)


def test_world_to_unity_axes():
    # MediaPipe world: y down, z smaller = closer to the camera
    j = ps.extract_joints(screen_lms(), world_lms({0: (0.1, -0.6, -0.2)}), False)[0]
    assert j[2:5] == pytest.approx((0.1, 0.6, -0.2))
    j = ps.extract_joints(screen_lms(), world_lms({0: (0.1, -0.6, -0.2)}), True)[0]
    assert j[2:5] == pytest.approx((-0.1, 0.6, -0.2))


def test_missing_world_sends_zero():
    j = ps.extract_joints(screen_lms(), None, True)[0]
    assert j[2:5] == (0.0, 0.0, 0.0)


def test_visibility_passed_through_even_when_low():
    joints = ps.extract_joints(screen_lms({28: (0.5, 0.5, 0.02)}), None, True)
    assert joints[12][5] == pytest.approx(0.02)


def test_mirror_keeps_body_side_labels():
    """Player's right wrist (MP 16) is on the image left; with mirror it shows on the wall right,
    and it is still sent as RightWrist (#6), not swapped with LeftWrist (#5)."""
    lms = screen_lms({RIGHT_WRIST_MP: (0.2, 0.3, 0.9), LEFT_WRIST_MP: (0.8, 0.6, 0.9)})
    wlms = world_lms({RIGHT_WRIST_MP: (-0.4, -0.5, 0.0), LEFT_WRIST_MP: (0.4, 0.1, 0.0)})
    for mirror, right_sx, right_wx in ((True, 0.8, 0.4), (False, 0.2, -0.4)):
        joints = ps.extract_joints(lms, wlms, mirror)
        right, left = joints[6], joints[5]
        assert right[0] == pytest.approx(right_sx)
        assert right[1] == pytest.approx(0.7)       # raised right arm -> high y
        assert right[2] == pytest.approx(right_wx)
        assert right[3] == pytest.approx(0.5)
        assert left[1] == pytest.approx(0.4)
        assert left[0] == pytest.approx(1.0 - right_sx)


def test_joint_order_follows_mediapipe_index():
    lms = [(i / 100.0, 0.0, 1.0) for i in range(33)]
    joints = ps.extract_joints(lms, None, False)
    assert [round(j[0] * 100) for j in joints] == list(ps.MEDIAPIPE_INDEX)


# ---------- streamer: count 0 once ----------

def test_streamer_sends_nothing_before_first_person():
    s = ps.PoseStreamer(True)
    assert s.update(None, None, 0) is None
    assert s.seq == 0


def test_streamer_count_zero_once_then_silent():
    s = ps.PoseStreamer(True)
    full = s.update(screen_lms(), world_lms(), 100)
    assert len(full) == 326
    empty = s.update(None, None, 140)
    assert empty == ps.pack_packet(2, 140, [])
    assert s.update(None, None, 180) is None
    assert s.update(None, None, 220) is None
    again = s.update(screen_lms(), world_lms(), 260)
    assert ps.unpack_packet(again)[0] == 3 and len(again) == 326
    assert ps.unpack_packet(s.update(None, None, 300)) == (4, 300, [])


def test_streamer_every_frame_while_present():
    s = ps.PoseStreamer(False)
    seqs = [ps.unpack_packet(s.update(screen_lms(), None, t))[0] for t in range(5)]
    assert seqs == [1, 2, 3, 4, 5]


# ---------- fake_pose_sender patterns ----------

import fake_pose_sender as fps  # noqa: E402


@pytest.mark.parametrize("pattern", fps.PATTERNS)
def test_fake_patterns_are_valid_packets(pattern):
    for t in (0.0, 0.7, 2.5, 5.1):
        joints = fps.to_joints(fps.pose_world(pattern, t))
        _seq, _ts, back = ps.unpack_packet(ps.pack_packet(1, 0, joints))
        assert len(back) == 13
        assert all(0.0 <= j[0] <= 1.0 and 0.0 <= j[1] <= 1.0 for j in back)


def test_fake_raise_alternates_sides():
    right_up = fps.to_joints(fps.pose_world("raise", 0.5))
    left_up = fps.to_joints(fps.pose_world("raise", 2.5))
    # RightWrist #6 above the nose on the screen right, then LeftWrist #5 on the screen left
    assert right_up[6][1] > right_up[0][1] > right_up[5][1] and right_up[6][0] > 0.5
    assert left_up[5][1] > left_up[0][1] > left_up[6][1] and left_up[5][0] < 0.5
    assert right_up[6][3] > 0.5 and right_up[6][2] > 0.0
