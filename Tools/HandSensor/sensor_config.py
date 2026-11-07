"""config.json loading for the hand sensor (pure: json + os only, unit tested).

Two groups of fields:
- startup: camera / UDP. Read once; changes need a sensor restart.
- tuning:  hand speed parameters. Re-read while running when the file changes
           (Unity F1 panel saves config.json -> sensor applies without restart).
CLI options override tuning fields and pin them (config changes are then ignored for that field).
The sensor never writes config.json.
"""
import json
import os

STARTUP_DEFAULTS = {
    "cameraIndex": 0,
    "cameraWidth": 640,
    "cameraHeight": 480,
    "mirror": True,
    "udpIp": "127.0.0.1",
    "udpPort": 9000,
    "udpBodyPort": 9001,
    "udpPosePort": 9002,     # pose joint stream, 0 = off
}

EXPOSURE_MIN = -13.0
EXPOSURE_MAX = 0.0

# config key -> default
TUNING_DEFAULTS = {
    "handSpeedRef": 1.5,     # speed (screen widths/s) mapped to v = 1
    "handMinSpeed": 0.05,    # slower hands are not sent
    "handEmitStill": False,  # send still hands with a small v
}


def parse_exposure(text):
    """--exposure value: "auto" or manual exposure as log2 seconds (-7 = 1/128 s). Raises ValueError otherwise."""
    if text.strip().lower() == "auto":
        return "auto"
    value = float(text)
    if not EXPOSURE_MIN <= value <= EXPOSURE_MAX:
        raise ValueError("exposure out of range: %r" % (text,))
    return value


def read_json(path):
    """Returns (dict or None, error message or None). Accepts UTF-8 with or without BOM."""
    try:
        with open(path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
    except FileNotFoundError:
        return None, "not found"
    except (OSError, ValueError) as e:
        return None, str(e)
    if not isinstance(data, dict):
        return None, "root is not an object"
    return data, None


def _coerce(value, default):
    """Convert a JSON value to the type of default. Raises ValueError/TypeError when it does not fit."""
    if isinstance(default, bool):
        if isinstance(value, bool):
            return value
        raise ValueError("expected true/false")
    if isinstance(value, bool):
        raise ValueError("expected a number")
    if isinstance(default, float):
        return float(value)
    if isinstance(default, int):
        return int(value)
    return type(default)(value)


def _valid_tuning(key, value):
    if key == "handSpeedRef":
        return value > 0.0
    if key == "handMinSpeed":
        return value >= 0.0
    return True


def parse_startup(data):
    """data: dict or None. Returns (cfg, warnings)."""
    cfg = dict(STARTUP_DEFAULTS)
    warnings = []
    if not data:
        return cfg, warnings
    for key, default in STARTUP_DEFAULTS.items():
        if key in data:
            try:
                cfg[key] = _coerce(data[key], default)
            except (TypeError, ValueError):
                warnings.append("bad value %s=%r -> %r" % (key, data[key], default))
    if cfg["udpBodyPort"] == cfg["udpPort"]:
        # same rule as Unity ConfigService.Sanitize, so both ends agree on the port
        port = cfg["udpPort"]
        cfg["udpBodyPort"] = port + 1 if port < 65535 else port - 1
        warnings.append("udpBodyPort must differ from udpPort (%d) -> %d" % (port, cfg["udpBodyPort"]))
    # same rule as Unity ConfigService.Sanitize
    pose_port = min(max(cfg["udpPosePort"], 0), 65535)
    if pose_port != 0 and pose_port in (cfg["udpPort"], cfg["udpBodyPort"]):
        warnings.append("udpPosePort must differ from udpPort and udpBodyPort (%d) -> pose stream off" % pose_port)
        pose_port = 0
    cfg["udpPosePort"] = pose_port
    return cfg, warnings


def merge_tuning(data, current, pinned):
    """Apply tuning fields from data onto current (dict, returns a new dict).

    Missing fields keep the current value; invalid fields keep the current value and add a warning;
    keys in pinned (set by CLI) are never changed.
    Returns (new_tuning, changed_keys, warnings).
    """
    new = dict(current)
    changed = []
    warnings = []
    if not data:
        return new, changed, warnings
    for key, default in TUNING_DEFAULTS.items():
        if key in pinned or key not in data:
            continue
        try:
            value = _coerce(data[key], default)
        except (TypeError, ValueError):
            warnings.append("bad value %s=%r - kept %r" % (key, data[key], current[key]))
            continue
        if not _valid_tuning(key, value):
            warnings.append("out of range %s=%r - kept %r" % (key, value, current[key]))
            continue
        if value != current[key]:
            new[key] = value
            changed.append(key)
    return new, changed, warnings


class ConfigWatcher:
    """Polls the config file mtime; returns parsed data when it changed and parses cleanly.

    A half-written file (parse error) is skipped and retried on the next poll.
    """

    def __init__(self, path):
        self.path = path
        self._mtime = self._stat()

    def _stat(self):
        try:
            return os.stat(self.path).st_mtime_ns
        except OSError:
            return None

    def poll(self):
        mtime = self._stat()
        if mtime is None or mtime == self._mtime:
            return None, None
        data, err = read_json(self.path)
        if data is None:
            return None, err  # keep old mtime -> retry next poll
        self._mtime = mtime
        return data, None
