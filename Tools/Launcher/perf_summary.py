#!/usr/bin/env python3
"""Summarize a PerfProbe CSV (game started with -perfLog <csv>). Standard library only.

Usage: python perf_summary.py perf.csv [--skip-sec 30]

Per phase: frames, average fps (total frames / total time), 1% low (worst and frame-weighted mean of the
per-row values), worst frame, frames with GC allocation and bytes (development build only).
Memory: first / last / max of each counter and the linear trend in MB per hour (whole run after --skip-sec).
"""
import argparse
import csv
import sys


def linear_slope(xs, ys):
    n = len(xs)
    if n < 2:
        return 0.0
    mx = sum(xs) / n
    my = sum(ys) / n
    den = sum((x - mx) ** 2 for x in xs)
    return sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / den if den else 0.0


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("csv")
    p.add_argument("--skip-sec", type=float, default=30.0, help="ignore rows before this time (startup)")
    args = p.parse_args(argv)

    with open(args.csv, encoding="utf-8") as f:
        rows = [r for r in csv.DictReader(f)]
    rows = [r for r in rows if float(r["t_sec"]) >= args.skip_sec and int(r["frames"]) > 0]
    if not rows:
        print("no rows after %.0f s" % args.skip_sec)
        return 1

    t0, t1 = float(rows[0]["t_sec"]), float(rows[-1]["t_sec"])
    print("rows %d, %.0f s .. %.0f s (%.2f h)" % (len(rows), t0, t1, (t1 - t0) / 3600.0))
    print("%-8s %9s %8s %9s %10s %8s %9s %11s" % ("phase", "frames", "avg fps", "1%low min", "1%low mean",
                                                    "max ms", "gc frames", "gc bytes"))
    phases = {}
    for r in rows:
        ph = phases.setdefault(r["phase"], {"frames": 0, "sec": 0.0, "p1": [], "p1w": 0.0, "max": 0.0,
                                            "gcf": 0, "gcb": 0, "gc_valid": True})
        n = int(r["frames"])
        fps = float(r["avg_fps"])
        ph["frames"] += n
        ph["sec"] += n / fps if fps > 0 else 0.0
        ph["p1"].append(float(r["p1_low_fps"]))
        ph["p1w"] += float(r["p1_low_fps"]) * n
        ph["max"] = max(ph["max"], float(r["max_ms"]))
        if int(r["gc_frames"]) < 0:
            ph["gc_valid"] = False
        else:
            ph["gcf"] += int(r["gc_frames"])
            ph["gcb"] += int(r["gc_bytes"])
    for name in ("wall", "awaken", "combat", "result", "other"):
        ph = phases.get(name)
        if not ph:
            continue
        print("%-8s %9d %8.1f %9.1f %10.1f %8.2f %9s %11s" % (
            name, ph["frames"], ph["frames"] / ph["sec"] if ph["sec"] else 0.0, min(ph["p1"]),
            ph["p1w"] / ph["frames"], ph["max"],
            ph["gcf"] if ph["gc_valid"] else "n/a", ph["gcb"] if ph["gc_valid"] else "n/a"))

    hours = [(float(r["t_sec"]) - t0) / 3600.0 for r in rows]
    for key in ("total_used_mb", "gc_reserved_mb", "system_used_mb"):
        ys = [float(r[key]) for r in rows]
        if ys[0] < 0:
            continue
        print("%-15s first %8.1f  last %8.1f  max %8.1f  trend %+7.2f MB/h" % (
            key, ys[0], ys[-1], max(ys), linear_slope(hours, ys)))
    print("gc collections %s -> %s, combats %s, results %s" % (
        rows[0]["gc_count"], rows[-1]["gc_count"], rows[-1]["combats"], rows[-1]["results"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
