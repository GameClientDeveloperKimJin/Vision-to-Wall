using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// One Euro filter for a <see cref="Vector3"/> (Casiez et al. 2012): cutoff = <see cref="MinCutoffHz"/> +
    /// <see cref="Beta"/> * speed. Still values are smoothed hard (less jitter), fast ones lightly (less lag).
    /// Same filter and defaults as the mini character (R3 <c>PoseSmoother</c>), kept here because Game must not
    /// reference UI. Beta is per meter/second (R3 uses body units; 0.8 per torso/s is about 1.8 per m/s).
    /// No allocation.
    /// </summary>
    public struct AvatarOneEuro
    {
        public const float MinCutoffHz = 1.2f;
        public const float Beta = 1.8f;
        public const float DerivativeCutoffHz = 1f;

        private Vector3 _value;
        private Vector3 _speed;
        private bool _primed;

        public Vector3 Value => _value;
        public bool Primed => _primed;

        public void Reset(Vector3 value)
        {
            _value = value;
            _speed = Vector3.zero;
            _primed = true;
        }

        public void Clear() => _primed = false;

        public Vector3 Filter(Vector3 sample, float dt)
        {
            if (!_primed)
            {
                Reset(sample);
                return _value;
            }

            if (dt <= 0f) return _value;
            _speed += ((sample - _value) / dt - _speed) * Alpha(dt, DerivativeCutoffHz);
            float cutoff = MinCutoffHz + Beta * _speed.magnitude;
            _value += (sample - _value) * Alpha(dt, cutoff);
            return _value;
        }

        public static float Alpha(float dt, float cutoffHz)
        {
            float tau = 1f / (2f * Mathf.PI * cutoffHz);
            return 1f / (1f + tau / dt);
        }
    }
}
