#!/usr/bin/env python3
"""Long run of the game build with fake input (no camera). Standard library only.

Starts Tools/FakeSender/fake_sender.py (hand stream, waves for --hand-on s then stops for --hand-off s, so the game
cycles media wall -> awaken -> combat -> result) and Tools/HandSensor/fake_pose_sender.py (joint stream), runs the
build with -perfLog, and records crashes. The game is restarted if it exits early (counted as a crash).
Do not run it together with the real hand sensor (same UDP ports).

Usage:
  python soak_run.py --exe ../../Builds/Windows/Dev/VisionToWall.exe --hours 2 --out logs/soak
  python perf_summary.py logs/soak/perf.csv
"""
import argparse
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
HAND_SENDER = os.path.join(ROOT, "Tools", "FakeSender", "fake_sender.py")
POSE_SENDER = os.path.join(ROOT, "Tools", "HandSensor", "fake_pose_sender.py")


def log(path, msg):
    line = "%s  %s" % (time.strftime("%Y-%m-%d %H:%M:%S"), msg)
    print(line, flush=True)
    with open(path, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def start_game(exe, out_dir, n, extra):
    args = [exe, "-perfLog", os.path.join(out_dir, "perf.csv"), "-logFile",
            os.path.join(out_dir, "game_%02d.log" % n)] + extra
    return subprocess.Popen(args, cwd=os.path.dirname(exe))


def close_game(p, wait=15):
    if p.poll() is not None:
        return
    subprocess.run(["taskkill", "/PID", str(p.pid)], capture_output=True)  # WM_CLOSE: normal quit
    try:
        p.wait(wait)
    except subprocess.TimeoutExpired:
        subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], capture_output=True)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--exe", required=True)
    ap.add_argument("--hours", type=float, default=2.0)
    ap.add_argument("--out", default=os.path.join(HERE, "logs", "soak"))
    ap.add_argument("--hand-on", type=float, default=20.0, help="seconds of waving per cycle")
    ap.add_argument("--hand-off", type=float, default=70.0, help="seconds without hands per cycle")
    ap.add_argument("--python", default=sys.executable)
    ap.add_argument("--game-args", nargs=argparse.REMAINDER, default=[])
    args = ap.parse_args(argv)

    exe = os.path.abspath(args.exe)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    runlog = os.path.join(out, "soak.log")
    senders = [
        subprocess.Popen([args.python, HAND_SENDER, "127.0.0.1", "9000", "30", "--on", str(args.hand_on),
                          "--off", str(args.hand_off)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL),
        subprocess.Popen([args.python, POSE_SENDER, "127.0.0.1", "9002", "30"],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL),
    ]
    log(runlog, "soak start: %s, %.2f h, hand %g s on / %g s off" % (exe, args.hours, args.hand_on, args.hand_off))
    end = time.time() + args.hours * 3600.0
    crashes = 0
    n = 1
    game = start_game(exe, out, n, args.game_args)
    log(runlog, "game pid %d" % game.pid)
    try:
        while time.time() < end:
            time.sleep(5)
            code = game.poll()
            if code is not None:
                crashes += 1
                log(runlog, "game exited early (code %s) - restart #%d" % (code, crashes))
                n += 1
                game = start_game(exe, out, n, args.game_args)
            for s in senders:
                if s.poll() is not None:
                    log(runlog, "sender exited (code %s)" % s.returncode)
    except KeyboardInterrupt:
        log(runlog, "interrupted")
    finally:
        close_game(game)
        for s in senders:
            s.terminate()
        log(runlog, "soak end: crashes %d, game exit code %s" % (crashes, game.returncode))
    return 0


if __name__ == "__main__":
    sys.exit(main())
