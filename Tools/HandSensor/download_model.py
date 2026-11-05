#!/usr/bin/env python3
"""Download the official MediaPipe hand_landmarker.task model into models/ (skipped if present).

Usage:
  python download_model.py            # -> models/hand_landmarker.task
  python download_model.py --force    # re-download
"""
import argparse
import os
import sys
import urllib.request

MODEL_URL = ("https://storage.googleapis.com/mediapipe-models/"
             "hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task")
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PATH = os.path.join(HERE, "models", "hand_landmarker.task")
MIN_VALID_BYTES = 1024 * 1024  # real model is ~7.5 MB; anything tiny is an error page


def download(path, force=False):
    if os.path.isfile(path) and os.path.getsize(path) >= MIN_VALID_BYTES and not force:
        print("model already present: %s (%d bytes) - skipped" % (path, os.path.getsize(path)))
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".part"
    print("downloading %s" % MODEL_URL)
    urllib.request.urlretrieve(MODEL_URL, tmp)
    size = os.path.getsize(tmp)
    if size < MIN_VALID_BYTES:
        os.remove(tmp)
        raise RuntimeError("downloaded file too small (%d bytes)" % size)
    os.replace(tmp, path)
    print("saved %s (%d bytes)" % (path, size))
    return path


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--out", default=DEFAULT_PATH)
    p.add_argument("--force", action="store_true")
    args = p.parse_args(argv)
    try:
        download(args.out, args.force)
    except Exception as e:  # network / disk errors -> clear message, non-zero exit
        print("download failed: %s" % e, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
