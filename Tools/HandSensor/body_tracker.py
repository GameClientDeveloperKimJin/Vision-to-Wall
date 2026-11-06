"""Body position from pose landmarks -> (x, y, v) for the body stream. Pure (no camera / MediaPipe), unit tested.

Center = mean of the visible shoulders and hips. A seated player has no visible hips, so shoulders
alone are enough; without any visible shoulder the frame counts as "nobody".
"""
import collections

import motion_packet as mpk

LEFT_SHOULDER = 11
RIGHT_SHOULDER = 12
LEFT_HIP = 23
RIGHT_HIP = 24
SHOULDERS = (LEFT_SHOULDER, RIGHT_SHOULDER)
HIPS = (LEFT_HIP, RIGHT_HIP)

VISIBILITY_MIN = 0.5
SPEED_REF = 1.0              # body speed (screen widths/s) mapped to v = 1
SPEED_SMOOTHING = 0.5        # EMA alpha for speed, damps landmark jitter

THROTTLE_ENTER_MS = 40.0     # hand + pose inference above this: pose runs every other frame
THROTTLE_EXIT_MS = 30.0      # below this: pose runs every frame again (gap avoids flapping)
THROTTLE_WINDOW = 30         # samples in the moving average; no decision until it is full (model warm-up)


def _visible(landmarks, indices):
    points = []
    for i in indices:
        x, y, visibility = landmarks[i]
        if visibility is not None and visibility >= VISIBILITY_MIN:
            points.append((x, y))
    return points


def body_center(landmarks):
    """landmarks: indexable sequence of 33 (x, y, visibility) in MediaPipe coords.
    Returns the (x, y) center in MediaPipe coords, or None when no shoulder is visible."""
    shoulders = _visible(landmarks, SHOULDERS)
    if not shoulders:
        return None
    points = shoulders + _visible(landmarks, HIPS)
    n = float(len(points))
    return sum(p[0] for p in points) / n, sum(p[1] for p in points) / n


class BodyTracker:
    def __init__(self, mirror):
        self.mirror = mirror
        self.reset()

    def reset(self):
        self._pos = None
        self._t = 0.0
        self._speed = 0.0

    def update(self, landmarks, now, aspect):
        """landmarks: as in body_center, or None when no pose was detected.
        Returns (point, center): point = (x, y, v) in wall coords, center = (x, y) in MediaPipe coords.
        Both are None when nobody is visible."""
        center = body_center(landmarks) if landmarks is not None else None
        if center is None:
            self.reset()
            return None, None
        if self._pos is not None:
            dt = now - self._t
            if dt > 0:
                raw = mpk.normalized_distance(center, self._pos, aspect) / dt
                self._speed += SPEED_SMOOTHING * (raw - self._speed)
        self._pos = center
        self._t = now
        x, y = mpk.to_output_coords(center[0], center[1], self.mirror)
        return (x, y, mpk.intensity(self._speed, SPEED_REF)), center


class PoseThrottle:
    """Runs pose every other frame while inference is slow.

    Based on inference time, not fps: a dim room limits the camera to about 15 fps while the CPU has
    room to spare, and skipping pose then would only delay the body stream.
    """

    def __init__(self):
        self.active = False
        self.average_ms = 0.0
        self._run_next = True
        self._samples = collections.deque(maxlen=THROTTLE_WINDOW)

    def add_inference(self, total_ms):
        """total_ms: hand + pose inference time of one frame. Only call on frames where pose ran."""
        self._samples.append(total_ms)
        self.average_ms = sum(self._samples) / len(self._samples)
        if len(self._samples) < THROTTLE_WINDOW:
            return
        if self.average_ms > THROTTLE_ENTER_MS:
            self.active = True
        elif self.average_ms < THROTTLE_EXIT_MS:
            self.active = False

    def should_run(self):
        if not self.active:
            self._run_next = True
            return True
        run = self._run_next
        self._run_next = not run
        return run
