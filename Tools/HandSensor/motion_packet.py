"""Pure helpers for the hand sensor: packet packing, coordinate mapping, intensity, hand matching.

No camera / MediaPipe / socket dependencies, so everything here is unit tested
(tests/test_motion_packet.py).

Wire format = Core MotionProtocol (little-endian), same as Tools/FakeSender/fake_sender.py:
  uint32 magic | uint32 seq | uint32 timestampMs | uint16 count | count * (float32 x, float32 y, float32 v)
"""
import math
import struct

MAGIC = 0x57325601
HEADER = struct.Struct("<IIIH")
POINT = struct.Struct("<fff")
MAX_POINTS = 4096
UINT32_MASK = 0xFFFFFFFF

# MediaPipe hand landmark indices
WRIST = 0
PALM_INDICES = (0, 5, 9, 13, 17)      # wrist + 4 finger MCP joints
TIP_INDICES = (4, 8, 12, 16, 20)      # thumb, index, middle, ring, pinky tips

POINT_MODES = ("palm", "tips", "all")


def clamp01(value):
    if value < 0.0:
        return 0.0
    if value > 1.0:
        return 1.0
    return value


def pack_packet(seq, timestamp_ms, points):
    """points: sequence of (x, y, v). Returns the UDP payload bytes."""
    count = len(points)
    if count > MAX_POINTS:
        raise ValueError("too many points: %d > %d" % (count, MAX_POINTS))
    buf = bytearray(HEADER.size + POINT.size * count)
    HEADER.pack_into(buf, 0, MAGIC, seq & UINT32_MASK, timestamp_ms & UINT32_MASK, count)
    offset = HEADER.size
    for x, y, v in points:
        POINT.pack_into(buf, offset, x, y, v)
        offset += POINT.size
    return bytes(buf)


def to_output_coords(x_mp, y_mp, mirror):
    """MediaPipe image coords (x right, y down, 0..1) -> wall coords (x 0 left..1 right, y 0 bottom..1 top).

    mirror=True flips x so the wall behaves like a mirror. Out-of-range values are clamped.
    """
    x = 1.0 - x_mp if mirror else x_mp
    y = 1.0 - y_mp
    return clamp01(x), clamp01(y)


def palm_center(landmarks):
    """landmarks: indexable sequence of 21 (x, y) in MediaPipe coords. Returns mean of PALM_INDICES."""
    sx = 0.0
    sy = 0.0
    for i in PALM_INDICES:
        x, y = landmarks[i]
        sx += x
        sy += y
    n = float(len(PALM_INDICES))
    return sx / n, sy / n


def tip_points(landmarks):
    return [tuple(landmarks[i][:2]) for i in TIP_INDICES]


def hand_points(landmarks, mode):
    """Source points (MediaPipe coords) for one hand according to --points mode."""
    if mode == "palm":
        return [palm_center(landmarks)]
    if mode == "tips":
        return tip_points(landmarks)
    if mode == "all":
        return [palm_center(landmarks)] + tip_points(landmarks)
    raise ValueError("unknown points mode: %r" % (mode,))


def intensity(speed, speed_ref):
    """v = clamp01(speed / speed_ref)."""
    if speed_ref <= 0.0:
        return 1.0 if speed > 0.0 else 0.0
    return clamp01(speed / speed_ref)


def normalized_distance(a, b, aspect):
    """Distance in screen-width units. aspect = frame height / width (y is scaled to width units)."""
    dx = a[0] - b[0]
    dy = (a[1] - b[1]) * aspect
    return math.hypot(dx, dy)


def match_hands(prev, cur, max_dist, label_penalty, aspect):
    """Greedy nearest matching of current hands to previous hands.

    prev, cur: lists of (palm_xy, label). Distance is in screen-width units; a different
    handedness label adds label_penalty. Pairs farther than max_dist are not matched.
    Returns list (len(cur)) of matched prev index or None (= new hand).
    """
    candidates = []
    for ci, (cpos, clabel) in enumerate(cur):
        for pi, (ppos, plabel) in enumerate(prev):
            d = normalized_distance(cpos, ppos, aspect)
            if d > max_dist:
                continue
            cost = d + (label_penalty if clabel != plabel else 0.0)
            candidates.append((cost, ci, pi))
    candidates.sort()

    result = [None] * len(cur)
    used_prev = set()
    for _cost, ci, pi in candidates:
        if result[ci] is not None or pi in used_prev:
            continue
        result[ci] = pi
        used_prev.add(pi)
    return result
