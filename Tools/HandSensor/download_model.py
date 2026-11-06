#!/usr/bin/env python3
"""Download the official MediaPipe hand and pose models into models/ (skipped if present).

Usage:
  python download_model.py            # -> models/hand_landmarker.task, models/pose_landmarker_lite.task
  python download_model.py --force    # re-download
"""
import argparse
import os
import sys
import urllib.request

MODEL_BASE = "https://storage.googleapis.com/mediapipe-models/"
MODELS = {
    "hand_landmarker.task": MODEL_BASE + "hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task",
    "pose_landmarker_lite.task": MODEL_BASE + "pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task",
}
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_DIR = os.path.join(HERE, "models")
MIN_VALID_BYTES = 1024 * 1024  # real models are 5 MB or more; anything tiny is an error page


def download(url, path, force=False):
    if os.path.isfile(path) and os.path.getsize(path) >= MIN_VALID_BYTES and not force:
        print("model already present: %s (%d bytes) - skipped" % (path, os.path.getsize(path)))
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".part"
    print("downloading %s" % url)
    urllib.request.urlretrieve(url, tmp)
    size = os.path.getsize(tmp)
    if size < MIN_VALID_BYTES:
        os.remove(tmp)
        raise RuntimeError("downloaded file too small (%d bytes)" % size)
    os.replace(tmp, path)
    print("saved %s (%d bytes)" % (path, size))
    return path


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--dir", default=DEFAULT_DIR, help="output folder")
    p.add_argument("--force", action="store_true")
    args = p.parse_args(argv)
    failed = 0
    for name, url in MODELS.items():  # one failure must not block the other model
        try:
            download(url, os.path.join(args.dir, name), args.force)
        except Exception as e:  # network / disk errors -> clear message, non-zero exit
            print("download failed: %s: %s" % (name, e), file=sys.stderr)
            failed += 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
