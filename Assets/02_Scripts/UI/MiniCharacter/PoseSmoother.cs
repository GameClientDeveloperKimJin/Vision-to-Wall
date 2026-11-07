using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Pure pose filter for the mini character. Converts screen joints to body units (torso length = 1,
    /// origin at the body center, +x right, +y up) so the figure has the same size near or far, then applies
    /// per-joint exponential smoothing and visibility fades. No allocation after construction.
    /// <para>
    /// Visibility below <see cref="VisibleThreshold"/>: the joint keeps its last position for <see cref="HoldSec"/>,
    /// then fades to <see cref="DimAlpha"/>. Count 0 or no packet for <see cref="LostTimeoutSec"/>: <see cref="Presence"/>
    /// fades to 0, and the next appearance snaps instead of flying in.
    /// </para>
    /// </summary>
    public sealed class PoseSmoother
    {
        public const float VisibleThreshold = 0.5f;
        public const float HoldSec = 0.3f;
        public const float LostTimeoutSec = 0.5f;
        /// <summary>Standing distance from the lower ankle up to the body center, in torso lengths.</summary>
        public const float AnkleToCenter = 2.1f;
        /// <summary>Torso shorter than this (screen heights) is treated as a bad detection.</summary>
        public const float MinTorso = 0.02f;
        /// <summary>Unity frames longer than this are clamped (Play-entry spikes).</summary>
        public const float MaxFrameDeltaSec = 0.1f;

        public float JointTauSec = 0.06f;
        /// <summary>Body center and scale follow slowly so a squat or a sidestep still shows as motion.</summary>
        public float CenterTauSec = 0.5f;
        public float ScaleTauSec = 1f;
        public float JointFadeSec = 0.25f;
        public float DimAlpha = 0.2f;
        public float PresenceFadeSec = 0.3f;

        private const int N = PoseProtocol.JointCount;
        private const int LShoulder = (int)PoseJoint.LeftShoulder;
        private const int RShoulder = (int)PoseJoint.RightShoulder;
        private const int LHip = (int)PoseJoint.LeftHip;
        private const int RHip = (int)PoseJoint.RightHip;
        private const int LAnkle = (int)PoseJoint.LeftAnkle;
        private const int RAnkle = (int)PoseJoint.RightAnkle;

        private readonly Vector2[] _target = new Vector2[N];
        private readonly float[] _targetVis = new float[N];
        private readonly float[] _lastSeen = new float[N];
        private readonly Vector2[] _pos = new Vector2[N];
        private readonly float[] _alpha = new float[N];

        private float _aspect = 4f / 3f;
        private bool _hasPerson;
        private float _lastPacket = float.NegativeInfinity;
        private bool _settled;
        private Vector2 _center;
        private float _scale = 1f;
        private float _presence;

        public PoseSmoother() => Reset();

        /// <summary>Camera width / height; screen x is multiplied by this so body units are square.</summary>
        public float Aspect
        {
            get => _aspect;
            set => _aspect = value > 0f && !float.IsInfinity(value) ? value : 4f / 3f;
        }

        /// <summary>0..1 fade of the whole figure.</summary>
        public float Presence => _presence;
        /// <summary>True once a full torso was seen since the figure last disappeared.</summary>
        public bool Settled => _settled;
        /// <summary>Body center in aspect-corrected screen units (x 0..Aspect, y 0..1).</summary>
        public Vector2 Center => _center;
        /// <summary>Torso length in screen heights.</summary>
        public float Scale => _scale;

        /// <summary>Smoothed joint in body units.</summary>
        public Vector2 Joint(int index) => _pos[index];
        public Vector2 Joint(PoseJoint joint) => _pos[(int)joint];

        /// <summary>1 visible, fading to <see cref="DimAlpha"/> after a hold. Not multiplied by <see cref="Presence"/>.</summary>
        public float JointAlpha(int index) => _alpha[index];
        public float JointAlpha(PoseJoint joint) => _alpha[(int)joint];
        public float BoneAlpha(PoseJoint a, PoseJoint b) => Mathf.Min(_alpha[(int)a], _alpha[(int)b]);

        public void Reset()
        {
            _hasPerson = false;
            _lastPacket = float.NegativeInfinity;
            _settled = false;
            _center = Vector2.zero;
            _scale = 1f;
            _presence = 0f;
            for (int i = 0; i < N; i++)
            {
                _target[i] = Vector2.zero;
                _targetVis[i] = 0f;
                _lastSeen[i] = float.NegativeInfinity;
                _pos[i] = Vector2.zero;
                _alpha[i] = 0f;
            }
        }

        /// <summary>New packet. <paramref name="now"/> is Time.unscaledTime. The frame is not kept.</summary>
        public void Push(PoseFrame frame, float now)
        {
            if (frame == null) return;
            _lastPacket = now;
            _hasPerson = frame.HasPerson;
            if (!_hasPerson) return;

            for (int i = 0; i < N; i++)
            {
                Vector2 s = frame.Screen[i];
                float v = frame.Visibility[i];
                if (!IsFinite(s.x) || !IsFinite(s.y) || !IsFinite(v)) v = 0f;
                else _target[i] = new Vector2(s.x * _aspect, s.y);

                _targetVis[i] = v;
                if (v >= VisibleThreshold) _lastSeen[i] = now;
            }
        }

        /// <summary>Advances smoothing and fades. Call once per frame, also while the view is hidden.</summary>
        public void Tick(float now, float dt)
        {
            if (dt < 0f) dt = 0f;
            else if (dt > MaxFrameDeltaSec) dt = MaxFrameDeltaSec;

            bool present = _hasPerson && now - _lastPacket < LostTimeoutSec;
            if (present) UpdateBody(now, dt);

            float presenceTarget = present && _settled ? 1f : 0f;
            _presence = MoveTowards(_presence, presenceTarget, dt, PresenceFadeSec);
            if (_presence <= 0f && !present) _settled = false;
        }

        private void UpdateBody(float now, float dt)
        {
            bool torsoVisible = IsVisible(LShoulder) && IsVisible(RShoulder) && IsVisible(LHip) && IsVisible(RHip);
            bool snap = false;

            if (torsoVisible)
            {
                Vector2 shoulders = (_target[LShoulder] + _target[RShoulder]) * 0.5f;
                Vector2 hips = (_target[LHip] + _target[RHip]) * 0.5f;
                float torso = Vector2.Distance(shoulders, hips);

                if (torso >= MinTorso)
                {
                    Vector2 center = (shoulders + hips) * 0.5f;
                    float scale = _settled ? _scale : torso;

                    // Feet on the base: with an ankle in view, anchor the center height to it so a squat lowers the body.
                    float ankleY = float.PositiveInfinity;
                    if (IsVisible(LAnkle)) ankleY = _target[LAnkle].y;
                    if (IsVisible(RAnkle) && _target[RAnkle].y < ankleY) ankleY = _target[RAnkle].y;
                    if (!float.IsPositiveInfinity(ankleY)) center.y = ankleY + AnkleToCenter * scale;

                    if (!_settled)
                    {
                        _center = center;
                        _scale = torso;
                        _settled = true;
                        snap = true;
                    }
                    else
                    {
                        _center.x += (center.x - _center.x) * Blend(dt, CenterTauSec);
                        _center.y += (center.y - _center.y) * Blend(dt, CenterTauSec);
                        _scale += (torso - _scale) * Blend(dt, ScaleTauSec);
                    }
                }
            }

            if (!_settled) return;

            float k = Blend(dt, JointTauSec);
            float inv = 1f / _scale;
            for (int i = 0; i < N; i++)
            {
                Vector2 n = (_target[i] - _center) * inv;
                bool visible = IsVisible(i);

                if (snap) _pos[i] = n;
                else if (visible) _pos[i] += (n - _pos[i]) * k;

                float alphaTarget = visible || now - _lastSeen[i] <= HoldSec ? 1f : DimAlpha;
                _alpha[i] = snap ? alphaTarget : MoveTowards(_alpha[i], alphaTarget, dt, JointFadeSec);
            }
        }

        private bool IsVisible(int i) => _targetVis[i] >= VisibleThreshold;

        private static float Blend(float dt, float tau) => tau <= 0f ? 1f : 1f - Mathf.Exp(-dt / tau);

        private static float MoveTowards(float value, float target, float dt, float durationSec)
        {
            float step = durationSec <= 0f ? 1f : dt / durationSec;
            return Mathf.MoveTowards(value, target, step);
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
