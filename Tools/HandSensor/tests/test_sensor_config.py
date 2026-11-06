import json
import os
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import sensor_config as scfg  # noqa: E402


def defaults():
    return dict(scfg.TUNING_DEFAULTS)


# ---------- read_json ----------

def test_read_json_bom_and_missing(tmp_path):
    p = tmp_path / "config.json"
    p.write_bytes(b"\xef\xbb\xbf" + json.dumps({"udpPort": 9100}).encode("utf-8"))
    data, err = scfg.read_json(str(p))
    assert err is None and data["udpPort"] == 9100
    data, err = scfg.read_json(str(tmp_path / "nope.json"))
    assert data is None and err == "not found"


def test_read_json_broken(tmp_path):
    p = tmp_path / "config.json"
    p.write_text('{"udpPort": 90', encoding="utf-8")
    data, err = scfg.read_json(str(p))
    assert data is None and err


# ---------- parse_startup ----------

def test_startup_defaults_and_override():
    cfg, w = scfg.parse_startup({"cameraIndex": 1, "udpPort": 9100, "mirror": False})
    assert cfg["cameraIndex"] == 1 and cfg["udpPort"] == 9100 and cfg["mirror"] is False
    assert cfg["udpIp"] == "127.0.0.1" and w == []
    cfg, w = scfg.parse_startup(None)
    assert cfg == scfg.STARTUP_DEFAULTS


def test_startup_bad_value_warns():
    cfg, w = scfg.parse_startup({"udpPort": "abc", "mirror": "yes"})
    assert cfg["udpPort"] == 9000 and cfg["mirror"] is True
    assert len(w) == 2


def test_startup_body_port():
    cfg, w = scfg.parse_startup({"udpBodyPort": 9101})
    assert cfg["udpBodyPort"] == 9101 and w == []
    cfg, _ = scfg.parse_startup({})
    assert cfg["udpBodyPort"] == 9001


def test_startup_body_port_must_differ_from_motion_port():
    cfg, w = scfg.parse_startup({"udpPort": 9100, "udpBodyPort": 9100})
    assert cfg["udpBodyPort"] == 9101 and len(w) == 1
    cfg, _ = scfg.parse_startup({"udpPort": 65535, "udpBodyPort": 65535})
    assert cfg["udpBodyPort"] == 65534


# ---------- merge_tuning ----------

def test_tuning_applied():
    new, changed, w = scfg.merge_tuning(
        {"handSpeedRef": 1.0, "handMinSpeed": 0.08, "handEmitStill": True}, defaults(), set())
    assert new == {"handSpeedRef": 1.0, "handMinSpeed": 0.08, "handEmitStill": True}
    assert sorted(changed) == ["handEmitStill", "handMinSpeed", "handSpeedRef"]
    assert w == []


def test_tuning_int_accepted_as_float():
    new, changed, _ = scfg.merge_tuning({"handSpeedRef": 2}, defaults(), set())
    assert new["handSpeedRef"] == 2.0 and isinstance(new["handSpeedRef"], float)


def test_tuning_missing_keeps_current():
    cur = {"handSpeedRef": 0.7, "handMinSpeed": 0.1, "handEmitStill": True}
    new, changed, w = scfg.merge_tuning({"udpPort": 9000}, cur, set())
    assert new == cur and changed == [] and w == []


def test_tuning_unchanged_value_not_reported():
    new, changed, _ = scfg.merge_tuning({"handSpeedRef": 1.5}, defaults(), set())
    assert changed == []


def test_tuning_invalid_kept():
    new, changed, w = scfg.merge_tuning(
        {"handSpeedRef": 0, "handMinSpeed": -1, "handEmitStill": "yes"}, defaults(), set())
    assert new == defaults() and changed == [] and len(w) == 3


def test_tuning_bool_not_number():
    new, _, w = scfg.merge_tuning({"handSpeedRef": True}, defaults(), set())
    assert new["handSpeedRef"] == 1.5 and len(w) == 1


def test_tuning_pinned_ignored():
    cur = defaults()
    cur["handSpeedRef"] = 0.8
    new, changed, _ = scfg.merge_tuning({"handSpeedRef": 3.0, "handMinSpeed": 0.2}, cur, {"handSpeedRef"})
    assert new["handSpeedRef"] == 0.8 and new["handMinSpeed"] == 0.2
    assert changed == ["handMinSpeed"]


# ---------- ConfigWatcher ----------

def _write(p, obj, mtime_ns):
    p.write_text(json.dumps(obj), encoding="utf-8")
    os.utime(str(p), ns=(mtime_ns, mtime_ns))


def test_watcher_detects_change(tmp_path):
    p = tmp_path / "config.json"
    _write(p, {"handSpeedRef": 1.5}, 1_000_000_000)
    w = scfg.ConfigWatcher(str(p))
    assert w.poll() == (None, None)  # unchanged
    _write(p, {"handSpeedRef": 1.0}, 2_000_000_000)
    data, err = w.poll()
    assert err is None and data["handSpeedRef"] == 1.0
    assert w.poll() == (None, None)  # consumed


def test_watcher_retries_half_written(tmp_path):
    p = tmp_path / "config.json"
    _write(p, {"handSpeedRef": 1.5}, 1_000_000_000)
    w = scfg.ConfigWatcher(str(p))
    p.write_text('{"handSpeedRef": 1.', encoding="utf-8")
    os.utime(str(p), ns=(2_000_000_000, 2_000_000_000))
    data, err = w.poll()
    assert data is None and err
    _write(p, {"handSpeedRef": 1.2}, 3_000_000_000)
    data, err = w.poll()
    assert data["handSpeedRef"] == 1.2


def test_parse_exposure():
    assert scfg.parse_exposure("-7") == -7.0
    assert scfg.parse_exposure(" Auto ") == "auto"
    for bad in ("1", "-14", "fast"):
        with pytest.raises(ValueError):
            scfg.parse_exposure(bad)


def test_watcher_missing_file(tmp_path):
    w = scfg.ConfigWatcher(str(tmp_path / "nope.json"))
    assert w.poll() == (None, None)
