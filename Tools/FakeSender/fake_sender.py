#!/usr/bin/env python3
"""Vision-to-Wall fake motion sender (field check without Unity Sensor).

Sends packets in the MotionProtocol wire format (little-endian):
  uint32 magic | uint32 seq | uint32 timestampMs | uint16 count | count * (float x, float y, float v)

Points rotate on a circle around (0.5, 0.5). Default cycle: 5 s ON / 7 s OFF (OFF = count 0).

Usage:
  python fake_sender.py                      # 127.0.0.1 9000 60
  python fake_sender.py 192.168.0.10 9000 30
  python fake_sender.py 127.0.0.1 9000 60 --points 5 --on 5 --off 7 --duration 30
  python fake_sender.py --no-cycle           # continuous motion
Standard library only (Python 3.7+).
"""
import argparse
import math
import socket
import struct
import sys
import time

MAGIC = 0x57325601
HEADER = struct.Struct("<IIIH")
POINT = struct.Struct("<fff")
MAX_POINTS = 4096


def build_packet(seq, timestamp_ms, points):
    parts = [HEADER.pack(MAGIC, seq & 0xFFFFFFFF, timestamp_ms & 0xFFFFFFFF, len(points))]
    parts.extend(POINT.pack(x, y, v) for x, y, v in points)
    return b"".join(parts)


def circle_points(t, count, radius, speed_deg, intensity):
    base = math.radians(t * speed_deg)
    step = 2.0 * math.pi / count if count else 0.0
    return [
        (0.5 + math.cos(base + step * i) * radius, 0.5 + math.sin(base + step * i) * radius, intensity)
        for i in range(count)
    ]


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("ip", nargs="?", default="127.0.0.1")
    p.add_argument("port", nargs="?", type=int, default=9000)
    p.add_argument("hz", nargs="?", type=float, default=60.0)
    p.add_argument("--points", type=int, default=4, help="points on the circle (default 4)")
    p.add_argument("--radius", type=float, default=0.3)
    p.add_argument("--speed", type=float, default=90.0, help="degrees per second")
    p.add_argument("--intensity", type=float, default=1.0)
    p.add_argument("--on", type=float, default=5.0, help="ON seconds per cycle")
    p.add_argument("--off", type=float, default=7.0, help="OFF seconds per cycle (count 0)")
    p.add_argument("--no-cycle", action="store_true", help="continuous motion, no OFF phase")
    p.add_argument("--duration", type=float, default=0.0, help="stop after N seconds (0 = until Ctrl+C)")
    args = p.parse_args(argv)

    if args.hz <= 0:
        p.error("hz must be > 0")
    if not 0 <= args.points <= MAX_POINTS:
        p.error("points must be 0..%d" % MAX_POINTS)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.ip, args.port)
    interval = 1.0 / args.hz
    period = args.on + args.off

    print("fake_sender -> %s:%d @ %.1f Hz, %d points, cycle %s"
          % (args.ip, args.port, args.hz, args.points,
             "off" if args.no_cycle else "%.1fs ON / %.1fs OFF" % (args.on, args.off)))

    start = time.perf_counter()
    next_t = start
    last_report = start
    seq = 0
    errors = 0
    try:
        while True:
            now = time.perf_counter()
            t = now - start
            if args.duration and t >= args.duration:
                break

            on = args.no_cycle or period <= 0 or (t % period) < args.on
            pts = circle_points(t, args.points, args.radius, args.speed, args.intensity) if on else []

            seq += 1
            try:
                sock.sendto(build_packet(seq, int(time.monotonic() * 1000), pts), target)
            except OSError:
                errors += 1  # e.g. ICMP port unreachable on Windows when nothing is bound

            if now - last_report >= 1.0:
                print("seq=%d state=%s count=%d errors=%d" % (seq, "ON " if on else "OFF", len(pts), errors))
                last_report = now

            next_t += interval
            delay = next_t - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            else:
                next_t = time.perf_counter()  # fell behind: resync instead of bursting
    except KeyboardInterrupt:
        pass
    finally:
        sock.close()

    print("sent %d packets, %d errors" % (seq, errors))
    return 0


if __name__ == "__main__":
    sys.exit(main())
