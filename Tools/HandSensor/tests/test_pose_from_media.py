import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import pose_from_media as pfm  # noqa: E402
import pose_record as prec  # noqa: E402
import pose_stream as ps  # noqa: E402


def screen_lms(x=0.25):
    return [(x, 0.5, 0.9)] * 33


def world_lms():
    return [(0.1, -0.2, 0.3)] * 33


def test_frame_ms_matches_unity_floor():
    # PoseTestRenderer.FrameMs: floor(i * 1000 / fps)
    assert [pfm.frame_ms(i, 30.0) for i in range(5)] == [0, 33, 66, 100, 133]
    assert pfm.frame_ms(299, 30.0) == 9966


def test_list_frames_sorted(tmp_path):
    for name in ("frame_00010.png", "frame_00002.png", "frame_00001.png", "other.png"):
        (tmp_path / name).write_bytes(b"")
    names = [os.path.basename(p) for p in pfm.list_frames(str(tmp_path))]
    assert names == ["frame_00001.png", "frame_00002.png", "frame_00010.png"]


def test_process_uses_sensor_conversion_and_count0():
    calls = []

    def detect(rgb, ts):
        calls.append(ts)
        return (None, None) if rgb == "empty" else (screen_lms(), world_lms())

    rows = pfm.process(["a", "empty", "b"], detect, 30.0, mirror=True)
    assert [t for t, _ in rows] == [0, 33, 66]
    assert calls == [0, 33, 66]
    assert rows[1][1] == []
    assert len(rows[0][1]) == ps.JOINT_COUNT
    # identical to pose_stream.extract_joints (mirror flips x)
    assert rows[0][1] == ps.extract_joints(screen_lms(), world_lms(), True)
    sx, sy, wx, wy, wz, v = rows[0][1][0]
    assert abs(sx - 0.75) < 1e-6 and abs(wx + 0.1) < 1e-6 and abs(wy - 0.2) < 1e-6


def test_process_timestamps_strictly_increase_at_high_fps():
    seen = []
    pfm.process(range(4), lambda rgb, ts: (seen.append(ts), (None, None))[1], 2000.0, mirror=False)
    assert seen == sorted(set(seen)) and len(seen) == 4


def test_packets_follow_sensor_rule():
    j = ps.extract_joints(screen_lms(), world_lms(), True)
    rows = [(0, []), (33, j), (66, j), (100, []), (133, []), (166, j)]
    out = pfm.packets(rows)
    # nothing before the first person, one count 0 on disappearance, then nothing
    assert [t for t, _ in out] == [33, 66, 100, 166]
    counts = [len(ps.unpack_packet(d)[2]) for _t, d in out]
    assert counts == [13, 13, 0, 13]
    assert [ps.unpack_packet(d)[0] for _t, d in out] == [1, 2, 3, 4]


def test_output_is_pose_record_csv(tmp_path):
    j = ps.extract_joints(screen_lms(), world_lms(), True)
    rows = [(0, j), (33, [])]
    path = str(tmp_path / "detected.csv")
    prec.write_csv(path, rows)
    back = prec.read_csv(path)
    assert [t for t, _ in back] == [0, 33]
    assert len(back[0][1]) == 13 and back[1][1] == []


def test_parse_args_requires_one_source():
    import pytest

    with pytest.raises(SystemExit):
        pfm.parse_args([])
    with pytest.raises(SystemExit):
        pfm.parse_args(["x", "--all", "y"])
    assert pfm.parse_args(["--all", "root"]).all == "root"
