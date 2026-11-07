#!/usr/bin/env python3
"""Fake pose joint stream (udpPosePort, default 9002) for testing receivers (R3, R4) without a camera.

Sends PoseProtocol packets of a standing figure facing the wall. Coordinates are already in the final
form (mirror applied): the player's right side is on the screen right and at world +x.

Patterns:
  wave    right arm up, waving side to side; left arm down (default)
  raise   right arm up 2 s, then left arm up 2 s, repeat (checks left/right labels)
  jacks   both arms up and down together, legs open and close
Cycle: --on seconds of motion, then one count 0 packet and --off seconds of silence (like a player leaving).

Usage:
  python fake_pose_sender.py                         # 127.0.0.1 9002 30 Hz, wave
  python fake_pose_sender.py --pattern raise --no-cycle
  python fake_pose_sender.py 192.168.0.10 9002 30 --pattern jacks --duration 20
Standard library only (+ pose_stream.py, motion_packet.py next to this file).
Do not run together with hand_sensor.py on the same port (seq would clash).
"""
import argparse
import math
import socket
import sys
import time

import motion_packet as mpk
import pose_stream as ps

PATTERNS = ("wave", "raise", "jacks")

# body in meters, hip center origin, Unity axes (+x player's right as seen on the mirrored wall, +y up)
SHOULDER = (0.18, 0.50)
HIP = (0.10, 0.0)
UPPER_ARM = 0.28
FOREARM = 0.25
THIGH = 0.42
SHIN = 0.42
NOSE = (0.0, 0.68, -0.10)
SCREEN_SCALE = 0.32       # meters -> screen units
SCREEN_HIP = (0.5, 0.45)  # hip center on screen


def arm(side, angle_deg, bend_deg=0.0):
    """side +1 right / -1 left. angle from straight down, raised outward. Returns (elbow, wrist) as (x, y)."""
    sx, sy = side * SHOULDER[0], SHOULDER[1]
    a = math.radians(angle_deg)
    ex, ey = sx + side * UPPER_ARM * math.sin(a), sy - UPPER_ARM * math.cos(a)
    b = math.radians(angle_deg + bend_deg)
    return (ex, ey), (ex + side * FOREARM * math.sin(b), ey - FOREARM * math.cos(b))


def leg(side, spread_deg):
    hx, hy = side * HIP[0], HIP[1]
    a = math.radians(spread_deg)
    kx, ky = hx + side * THIGH * math.sin(a), hy - THIGH * math.cos(a)
    return (hx, hy), (kx, ky), (kx + side * SHIN * math.sin(a), ky - SHIN * math.cos(a))


def pose_world(pattern, t):
    """Returns 13 world (x, y, z) in packet order for time t (seconds)."""
    right_arm, right_bend, left_arm, left_bend, spread = 15.0, 0.0, 15.0, 0.0, 4.0
    if pattern == "wave":
        right_arm = 140.0
        right_bend = 25.0 * math.sin(2.0 * math.pi * 1.2 * t)
    elif pattern == "raise":
        if int(t // 2.0) % 2 == 0:
            right_arm = 170.0
        else:
            left_arm = 170.0
    elif pattern == "jacks":
        k = 0.5 - 0.5 * math.cos(2.0 * math.pi * 0.8 * t)
        right_arm = left_arm = 15.0 + 155.0 * k
        spread = 4.0 + 14.0 * k
    else:
        raise ValueError("unknown pattern: %r" % (pattern,))
    l_elbow, l_wrist = arm(-1, left_arm, left_bend)
    r_elbow, r_wrist = arm(+1, right_arm, right_bend)
    l_hip, l_knee, l_ankle = leg(-1, spread)
    r_hip, r_knee, r_ankle = leg(+1, spread)
    flat = [(-SHOULDER[0], SHOULDER[1]), (SHOULDER[0], SHOULDER[1]), l_elbow, r_elbow, l_wrist, r_wrist,
            l_hip, r_hip, l_knee, r_knee, l_ankle, r_ankle]
    return [NOSE] + [(x, y, 0.0) for x, y in flat]


def to_joints(world):
    joints = []
    for wx, wy, wz in world:
        sx = mpk.clamp01(SCREEN_HIP[0] + wx * SCREEN_SCALE)
        sy = mpk.clamp01(SCREEN_HIP[1] + wy * SCREEN_SCALE)
        joints.append((sx, sy, wx, wy, wz, 0.99))
    return joints


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("ip", nargs="?", default="127.0.0.1")
    p.add_argument("port", nargs="?", type=int, default=9002)
    p.add_argument("hz", nargs="?", type=float, default=30.0)
    p.add_argument("--pattern", choices=PATTERNS, default="wave")
    p.add_argument("--on", type=float, default=8.0, help="seconds with a person per cycle")
    p.add_argument("--off", type=float, default=3.0, help="seconds with nobody per cycle (one count 0 packet)")
    p.add_argument("--no-cycle", action="store_true", help="person always present")
    p.add_argument("--duration", type=float, default=0.0, help="stop after N seconds (0 = until Ctrl+C)")
    args = p.parse_args(argv)
    if args.hz <= 0:
        p.error("hz must be > 0")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.ip, args.port)
    print("fake_pose_sender -> %s:%d %.0f Hz pattern=%s %s"
          % (args.ip, args.port, args.hz, args.pattern,
             "continuous" if args.no_cycle else "cycle %gs on / %gs off" % (args.on, args.off)), flush=True)
    seq = 0
    present = False
    start = time.monotonic()
    period = 1.0 / args.hz
    next_t = start
    try:
        while True:
            now = time.monotonic()
            t = now - start
            if args.duration > 0 and t >= args.duration:
                break
            cycle = args.on + args.off
            on = args.no_cycle or cycle <= 0 or (t % cycle) < args.on
            joints = to_joints(pose_world(args.pattern, t)) if on else []
            if on or present:  # nobody: count 0 once, then silence
                seq += 1
                try:
                    sock.sendto(ps.pack_packet(seq, int(now * 1000), joints), target)
                except OSError:
                    pass  # Windows ICMP port unreachable when nobody listens
                if not on:
                    print("t=%.1fs nobody (count 0 sent)" % t, flush=True)
            present = on
            next_t += period
            time.sleep(max(0.0, next_t - time.monotonic()))
    except KeyboardInterrupt:
        pass
    finally:
        sock.close()
    print("stopped: packets=%d" % seq, flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
