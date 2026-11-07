"""Pose joint stream (udpPosePort, default 9002). Pure (struct + motion_packet only), unit tested.

Wire format = Core PoseProtocol (little-endian), see Assets/05_Docs/52_세션R2_PoseStream.md:
  uint32 magic | uint32 seq | uint32 timestampMs | uint16 count (0 = nobody, else 13)
  | count * (float32 screenX, screenY, worldX, worldY, worldZ, visibility)

Screen: 0..1, origin bottom-left, mirror applied (same as ports 9000 / 9001).
World: MediaPipe pose_world_landmarks (meters, hip centered) in Unity axes:
  +x right (mirror applied), +y up, +z away from the camera.
Joint left/right is the player's own body side (MediaPipe label), never swapped by the mirror.
"""
import struct

import motion_packet as mpk

MAGIC = 0x57325602
HEADER = struct.Struct("<IIIH")
JOINT = struct.Struct("<6f")
UINT32_MASK = 0xFFFFFFFF

# packet order -> MediaPipe Pose landmark index (Core PoseProtocol.MediaPipeIndex)
JOINT_NAMES = (
    "Nose",
    "LeftShoulder", "RightShoulder",
    "LeftElbow", "RightElbow",
    "LeftWrist", "RightWrist",
    "LeftHip", "RightHip",
    "LeftKnee", "RightKnee",
    "LeftAnkle", "RightAnkle",
)
MEDIAPIPE_INDEX = (0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28)
JOINT_COUNT = len(MEDIAPIPE_INDEX)
PACKET_SIZE = HEADER.size + JOINT.size * JOINT_COUNT   # 326
EMPTY_PACKET_SIZE = HEADER.size                        # 14


def to_unity_world(x_mp, y_mp, z_mp, mirror):
    """MediaPipe world (x image right, y down, z smaller = closer to camera) -> Unity axes."""
    return (-x_mp if mirror else x_mp), -y_mp, z_mp


def extract_joints(screen, world, mirror):
    """screen: 33 (x, y, visibility) MediaPipe image landmarks. world: 33 (x, y, z) or None (model without world
    output -> world values 0). Returns JOINT_COUNT tuples (sx, sy, wx, wy, wz, visibility) in packet order."""
    joints = []
    for mp_index in MEDIAPIPE_INDEX:
        x, y, visibility = screen[mp_index]
        sx, sy = mpk.to_output_coords(x, y, mirror)
        if world is not None:
            wx, wy, wz = to_unity_world(world[mp_index][0], world[mp_index][1], world[mp_index][2], mirror)
        else:
            wx = wy = wz = 0.0
        joints.append((sx, sy, wx, wy, wz, visibility if visibility is not None else 0.0))
    return joints


def pack_packet(seq, timestamp_ms, joints):
    """joints: [] (nobody) or JOINT_COUNT tuples of 6 floats. Returns the UDP payload bytes."""
    count = len(joints)
    if count not in (0, JOINT_COUNT):
        raise ValueError("joint count must be 0 or %d, got %d" % (JOINT_COUNT, count))
    buf = bytearray(HEADER.size + JOINT.size * count)
    HEADER.pack_into(buf, 0, MAGIC, seq & UINT32_MASK, timestamp_ms & UINT32_MASK, count)
    offset = HEADER.size
    for j in joints:
        JOINT.pack_into(buf, offset, *j)
        offset += JOINT.size
    return bytes(buf)


def unpack_packet(data):
    """Returns (seq, timestamp_ms, joints) or raises ValueError. Used by pose_monitor.py and the tests."""
    if len(data) < HEADER.size:
        raise ValueError("short packet: %d bytes" % len(data))
    magic, seq, ts, count = HEADER.unpack_from(data, 0)
    if magic != MAGIC:
        raise ValueError("bad magic 0x%08X" % magic)
    if count not in (0, JOINT_COUNT):
        raise ValueError("bad count %d" % count)
    if len(data) != HEADER.size + JOINT.size * count:
        raise ValueError("size %d does not match count %d" % (len(data), count))
    joints = [JOINT.unpack_from(data, HEADER.size + JOINT.size * i) for i in range(count)]
    return seq, ts, joints


class PoseStreamer:
    """Decides what to send per pose inference: a full packet while a person is visible, one count 0 packet
    when the person disappears, then nothing until a person is seen again. Owns the stream's seq."""

    def __init__(self, mirror):
        self.mirror = mirror
        self.seq = 0
        self._present = False  # nothing is sent before the first person

    def update(self, screen, world, timestamp_ms):
        """screen / world: as in extract_joints; screen None = nobody. Returns packet bytes or None (send nothing)."""
        if screen is None:
            if not self._present:
                return None
            self._present = False
            joints = []
        else:
            self._present = True
            joints = extract_joints(screen, world, self.mirror)
        self.seq += 1
        return pack_packet(self.seq, timestamp_ms, joints)
