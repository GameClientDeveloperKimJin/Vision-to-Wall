#!/usr/bin/env python3
"""Pose joint stream receiver for checking R2 without Unity (udpPosePort, default 9002).

Prints the wrists, elbows and nose a few times per second, plus packet rate, seq gaps and latency
(capture -> receive, same PC only: both ends use time.monotonic()). Ctrl+C prints a summary.

Do not run while Unity listens on the same port (only one receiver can bind it).

Usage:
  python pose_monitor.py                    # 0.0.0.0:9002, 4 lines/s
  python pose_monitor.py --port 9002 --rate 10 --duration 20
  python pose_monitor.py --all              # all 13 joints per line
Standard library only (+ pose_stream.py next to this file).
"""
import argparse
import socket
import sys
import time

import pose_stream as ps

DEFAULT_PORT = 9002
SHOWN = ("Nose", "LeftWrist", "RightWrist", "LeftElbow", "RightElbow")


def short(name):
    return name.replace("Left", "L_").replace("Right", "R_")


def format_joint(name, j):
    sx, sy, wx, wy, wz, vis = j
    return "%s scr(%.2f,%.2f) w(%+.2f,%+.2f,%+.2f) v%.2f" % (short(name), sx, sy, wx, wy, wz, vis)


def format_frame(joints, names):
    if not joints:
        return "count=0 (nobody)"
    index = {n: i for i, n in enumerate(ps.JOINT_NAMES)}
    return " | ".join(format_joint(n, joints[index[n]]) for n in names)


class Stats:
    def __init__(self):
        self.packets = 0
        self.empty = 0
        self.bad = 0
        self.gaps = 0
        self.last_seq = None
        self.latency_sum = 0.0
        self.latency_max = 0.0
        self.latency_n = 0

    def add(self, seq, ts, joints, now_ms):
        self.packets += 1
        if not joints:
            self.empty += 1
        if self.last_seq is not None and seq != (self.last_seq + 1) & ps.UINT32_MASK:
            self.gaps += 1
        self.last_seq = seq
        latency = (now_ms - ts) & ps.UINT32_MASK
        if latency < 10000:  # ignore a sender on another PC / clock
            self.latency_sum += latency
            self.latency_max = max(self.latency_max, latency)
            self.latency_n += 1

    def latency_text(self):
        if not self.latency_n:
            return "latency n/a"
        return "latency avg %.1f ms max %.0f ms" % (self.latency_sum / self.latency_n, self.latency_max)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--port", type=int, default=DEFAULT_PORT)
    p.add_argument("--bind", default="0.0.0.0")
    p.add_argument("--rate", type=float, default=4.0, help="printed lines per second (default 4)")
    p.add_argument("--all", action="store_true", help="print all 13 joints")
    p.add_argument("--duration", type=float, default=0.0, help="stop after N seconds (0 = until Ctrl+C)")
    args = p.parse_args(argv)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.bind((args.bind, args.port))
    except OSError as e:
        print("bind %s:%d failed (%s) - is Unity or another monitor listening?" % (args.bind, args.port, e),
              file=sys.stderr)
        return 1
    sock.settimeout(0.2)
    names = ps.JOINT_NAMES if args.all else SHOWN
    print("pose_monitor on %s:%d - waiting for packets (Ctrl+C to stop)" % (args.bind, args.port), flush=True)

    stats = Stats()
    start = time.monotonic()
    next_print = start
    win_start = start
    win_packets = 0
    latest = None
    try:
        while not (args.duration > 0 and time.monotonic() - start >= args.duration):
            try:
                data, _ = sock.recvfrom(2048)
            except socket.timeout:
                data = None
            except OSError:
                continue  # Windows: ICMP reset from an earlier send, harmless
            now = time.monotonic()
            if data is not None:
                try:
                    seq, ts, joints = ps.unpack_packet(data)
                except ValueError as e:
                    stats.bad += 1
                    print("bad packet (%d bytes): %s" % (len(data), e), flush=True)
                    continue
                stats.add(seq, ts, joints, int(now * 1000))
                win_packets += 1
                if not joints:
                    print("seq=%d count=0 (nobody)" % seq, flush=True)
                latest = (seq, joints)
            if now >= next_print and latest is not None and latest[1]:
                rate = win_packets / (now - win_start) if now > win_start else 0.0
                print("seq=%d %4.1f/s %s" % (latest[0], rate, format_frame(latest[1], names)), flush=True)
                next_print = now + 1.0 / args.rate
                win_start = now
                win_packets = 0
    except KeyboardInterrupt:
        pass
    finally:
        sock.close()

    elapsed = time.monotonic() - start
    print("summary: %.1fs packets=%d (%.1f/s) count0=%d bad=%d seqGaps=%d %s"
          % (elapsed, stats.packets, stats.packets / elapsed if elapsed > 0 else 0.0, stats.empty, stats.bad,
             stats.gaps, stats.latency_text()), flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
