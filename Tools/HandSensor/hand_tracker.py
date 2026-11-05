"""Hand tracking across frames -> (x, y, v) wall points. Pure (no camera / MediaPipe), unit tested.

Continuity rules (so waving does not flicker):
- A hand that was moving keeps being sent for HOLD_SEC after it last moved, with the peak v
  fading out. Covers the near-zero speed at each turn of a wave.
- A hand lost by the detector (motion blur) is kept for TRACK_GRACE_SEC; while held it is sent
  at its last position, and when it is found again it continues as the same hand (no first-frame skip).
"""
import motion_packet as mpk

STILL_INTENSITY = 0.1        # v for still hands with emit_still
SPEED_SMOOTHING = 0.5        # EMA alpha for hand speed (1 = no smoothing), damps landmark jitter
MATCH_MAX_DIST = 0.25        # screen widths; farther = treated as a new hand
MATCH_LABEL_PENALTY = 0.1    # extra cost when handedness label differs
TRACK_GRACE_SEC = 0.3        # keep a lost hand this long
HOLD_SEC = 0.3               # keep sending a hand this long after it last moved
HOLD_MIN_FACTOR = 0.5        # held v fades from peak to peak * this at the end of HOLD_SEC


class HandTracker:
    def __init__(self, mode, mirror, tuning):
        self.mode = mode
        self.mirror = mirror
        self.tracks = []   # dicts: pos, label, speed, t (last seen), move_t, peak_v, points (wall), src (mp)
        self.held_frames = 0  # frames where at least one hand was sent only because of hold
        self.apply_tuning(tuning)

    def apply_tuning(self, tuning):
        self.speed_ref = tuning["handSpeedRef"]
        self.min_speed = tuning["handMinSpeed"]
        self.emit_still = tuning["handEmitStill"]

    def reset(self):
        self.tracks = []

    def _held_v(self, track, now):
        """v while held, or None when the hold has expired / hand never moved."""
        if track["move_t"] is None:
            return None
        age = now - track["move_t"]
        if age > HOLD_SEC:
            return None
        fade = 1.0 - (1.0 - HOLD_MIN_FACTOR) * (age / HOLD_SEC)
        return track["peak_v"] * fade

    def _emit(self, track, v, still, points, preview):
        for (x, y), (px, py) in zip(track["points"], track["src"]):
            points.append((x, y, v))
            preview.append((px, py, v, still))

    def update(self, hands, now, aspect):
        """hands: list of (landmarks_xy, label). Returns (points, preview):
        points = [(x, y, v)] wall coords, preview = [(x_mp, y_mp, v, still)]."""
        self.tracks = [t for t in self.tracks if now - t["t"] <= TRACK_GRACE_SEC]
        palms = [(mpk.palm_center(lm), label) for lm, label in hands]
        matches = mpk.match_hands([(t["pos"], t["label"]) for t in self.tracks], palms,
                                  MATCH_MAX_DIST, MATCH_LABEL_PENALTY, aspect)

        points = []
        preview = []
        new_tracks = []
        matched = set()
        held_any = False
        for (lm, label), (palm, _), m in zip(hands, palms, matches):
            src = mpk.hand_points(lm, self.mode)
            wall = [mpk.to_output_coords(px, py, self.mirror) for px, py in src]
            if m is None:
                # new hand: remember it, do not send on its first frame (no speed yet)
                new_tracks.append({"pos": palm, "label": label, "speed": 0.0, "t": now,
                                   "move_t": None, "peak_v": 0.0, "points": wall, "src": src})
                continue
            prev = self.tracks[m]
            matched.add(m)
            dt = now - prev["t"]
            raw = mpk.normalized_distance(palm, prev["pos"], aspect) / dt if dt > 0 else prev["speed"]
            speed = prev["speed"] + SPEED_SMOOTHING * (raw - prev["speed"])
            track = {"pos": palm, "label": label, "speed": speed, "t": now,
                     "move_t": prev["move_t"], "peak_v": prev["peak_v"], "points": wall, "src": src}
            new_tracks.append(track)

            held = self._held_v(track, now)
            if speed >= self.min_speed:
                v = mpk.intensity(speed, self.speed_ref)
                if held is None or v >= held:
                    # stronger than what is held -> new peak
                    track["move_t"] = now
                    track["peak_v"] = v
                    self._emit(track, v, False, points, preview)
                else:
                    # slowing down (smoothed speed decays) -> keep the held peak fading out
                    held_any = True
                    self._emit(track, held, False, points, preview)
                continue
            if held is not None:
                held_any = True
                self._emit(track, held, False, points, preview)
            elif self.emit_still:
                self._emit(track, STILL_INTENSITY, True, points, preview)

        # lost hands within the grace period: keep, and send at last position while held
        for i, t in enumerate(self.tracks):
            if i in matched:
                continue
            new_tracks.append(t)
            held = self._held_v(t, now)
            if held is not None:
                held_any = True
                self._emit(t, held, False, points, preview)
        self.tracks = new_tracks
        if held_any:
            self.held_frames += 1
        return points, preview
