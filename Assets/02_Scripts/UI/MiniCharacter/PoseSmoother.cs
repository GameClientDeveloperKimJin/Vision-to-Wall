using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Pure pose filter for the mini character. Uses the <b>world</b> joints (meters, hip centered, mirror applied,
    /// +x right, +y up; z ignored) rather than screen joints: MediaPipe estimates joints outside the camera frame
    /// in world space, so a player seen only from the waist up (desk test, close to the wall) still gets a full,
    /// correctly proportioned figure. Screen joints are clamped to 0..1 there and would collapse onto the frame edge.
    /// <para>
    /// Output is in body units (torso length = 1, origin at the body center). Per-joint exponential smoothing.
    /// Visibility below <see cref="VisibleThreshold"/>: the joint keeps its last position for <see cref="HoldSec"/>,
    /// then fades to <see cref="DimAlpha"/> and slowly follows the estimate. Count 0 or no packet for
    /// <see cref="LostTimeoutSec"/>: <see cref="Presence"/> fades to 0, and the next appearance snaps instead of flying in.
    /// </para>
    /// No allocation after construction.
    /// </summary>
    public sealed class PoseSmoother
    {
        public const float VisibleThreshold = 0.5f;
        public const float HoldSec = 0.3f;
        public const float LostTimeoutSec = 0.5f;
        /// <summary>Standing distance from the lower ankle up to the body center, in torso lengths.</summary>
        public const float AnkleToCenter = 2.1f;
        /// <summary>World torso (shoulder center to hip center) outside this range in meters is a bad detection.</summary>
        public const float MinTorsoM = 0.1f;
        public const float MaxTorsoM = 1f;
        /// <summary>Unity frames longer than this are clamped (Play-entry spikes).</summary>
        public const float MaxFrameDeltaSec = 0.1f;

        public float JointTauSec = 0.06f;
        /// <summary>Hidden joints follow the world estimate this slowly after the hold.</summary>
        public float HiddenJointTauSec = 0.3f;
        /// <summary>Body center and scale follow slowly so a squat still shows as motion.</summary>
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

        private bool _hasPerson;
        private float _lastPacket = float.NegativeInfinity;
        private bool _settled;
        private Vector2 _center;
        private float _scale = 1f;
        private float _presence;

        public PoseSmoother() => Reset();

        /// <summary>0..1 fade of the whole figure.</summary>
        public float Presence => _presence;
        /// <summary>True once both shoulders were seen since the figure last disappeared.</summary>
        public bool Settled => _settled;
        /// <summary>Body center in world meters (x, y).</summary>
        public Vector2 Center => _center;
        /// <summary>Torso length in meters.</summary>
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
                Vector3 w = frame.World[i];
                float v = frame.Visibility[i];
                if (!IsFinite(w.x) || !IsFinite(w.y) || !IsFinite(v)) v = 0f;
                else _target[i] = new Vector2(w.x, w.y);

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
            bool snap = false;

            // World hips are always estimated (hip-centered space), so only the shoulders must be in view.
            if (IsVisible(LShoulder) && IsVisible(RShoulder))
            {
                Vector2 shoulders = (_target[LShoulder] + _target[RShoulder]) * 0.5f;
                Vector2 hips = (_target[LHip] + _target[RHip]) * 0.5f;
                float torso = Vector2.Distance(shoulders, hips);

                if (torso >= MinTorsoM && torso <= MaxTorsoM)
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
            float kHidden = Blend(dt, HiddenJointTauSec);
            float inv = 1f / _scale;
            for (int i = 0; i < N; i++)
            {
                Vector2 n = (_target[i] - _center) * inv;
                bool visible = IsVisible(i);
                bool held = !visible && now - _lastSeen[i] <= HoldSec;

                if (snap) _pos[i] = n;
                else if (visible) _pos[i] += (n - _pos[i]) * k;
                else if (!held) _pos[i] += (n - _pos[i]) * kHidden;

                float alphaTarget = visible || held ? 1f : DimAlpha;
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
