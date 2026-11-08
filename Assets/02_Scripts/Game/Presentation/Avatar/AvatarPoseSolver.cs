using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>Limb bones the pose turns, in <see cref="AvatarPoseSolver"/> output order.</summary>
    public enum AvatarLimb
    {
        LeftUpperArm, LeftLowerArm, RightUpperArm, RightLowerArm,
        LeftUpperLeg, LeftLowerLeg, RightUpperLeg, RightLowerLeg,
    }

    /// <summary>
    /// Pure pose -> bone direction logic for the combat avatar. Input: <see cref="PoseFrame.World"/> (meters, hip
    /// centered, mirror applied: +x = the player's right = screen right). Output: limb and torso directions in the
    /// <b>avatar frame</b> (avatar seen from behind: its front +z into the screen, its right +x, up +y), so the
    /// player's right arm drives the avatar's right arm on the screen's right side. Mapping: (x, y, z) -> (x, y, -z)
    /// because the player faces the camera and the avatar faces away from it.
    /// <para>
    /// <b>Depth.</b> MediaPipe world z is biased (RT, doc 56: shoulders 15-22 cm toward the camera, ankles 9-21 cm
    /// away): raw z bends straight limbs 20-30 degrees and tilts the torso. So limbs use z scaled by
    /// <see cref="LimbDepthGain"/>, the torso ignores z (<see cref="TorsoDepthGain"/> 0), every limb's angle out of
    /// the frontal plane is clamped (no arm through the back), and thighs, whose forward swing (squat) shows mainly
    /// as foreshortening, get their depth back from the shortened frontal length (<see cref="ThighRecoverBelow"/>).
    /// Measured on the RT clips: see doc 54 work log and <c>AvatarPoseRtTests</c>.
    /// </para>
    /// Per-joint One Euro smoothing. A limb whose joints are not visible (below <see cref="VisibleThreshold"/>) for
    /// <see cref="HoldSec"/>, no person, or no packet for <see cref="LostTimeoutSec"/>: its weight goes to 0 over
    /// <see cref="ReturnSec"/> (back to the rest pose). No allocation after construction.
    /// </summary>
    public sealed class AvatarPoseSolver
    {
        public const int LimbCount = 8;
        public const float VisibleThreshold = 0.5f;
        public const float HoldSec = 0.3f;
        public const float ReturnSec = 0.3f;
        /// <summary>A limb seen again comes back this fast (R4b: 0.3 s made a raised arm sweep up from the rest pose).</summary>
        public float AppearSec = 0.1f;
        public const float LostTimeoutSec = 0.5f;
        public const float MaxFrameDeltaSec = 0.1f;

        /// <summary>Share of the measured limb depth kept. 1 = raw MediaPipe, 0 = frontal plane only.</summary>
        public float LimbDepthGain = 0.25f;
        /// <summary>Share of the measured torso lean (depth) kept. The raw lean is mostly bias (20+ degrees forward).</summary>
        public float TorsoDepthGain = 0f;
        /// <summary>Thigh frontal length over its longest seen length below which depth is rebuilt from the length.</summary>
        public float ThighRecoverBelow = 0.8f;
        public float ThighRecoverWidth = 0.25f;
        /// <summary>Thigh over torso length (frontal), lower bound for the thigh length before one was seen.</summary>
        public float ThighOverTorso = 0.9f;
        /// <summary>The longest seen lengths shrink this fast (share per second) so a new player re-measures.</summary>
        public float LengthDecayPerSec = 0.02f;
        /// <summary>Limits of a limb's angle out of the frontal plane. + = forward (toward the boss).</summary>
        public float MaxForwardDeg = 80f;
        public float MaxBackwardArmDeg = 30f;
        public float MaxBackwardThighDeg = 30f;
        public float MaxBackwardShinDeg = 85f;
        /// <summary>
        /// Limb heading on the screen plane from the camera 2D joints (what the player sees in the mirror) instead
        /// of the world x, y, when both joints are inside the image by <see cref="ScreenEdgeMargin"/>. World length
        /// and depth are kept. Each bone takes its own heading (R4b: per bone beat turning the whole arm by the upper
        /// arm's correction, 5.5 vs 9.5 degrees on the user recording, at the cost of the RT elbow angle 0.8 -> 6.3).
        /// R4b user recording: world x, y differ from the image by 20-40 degrees for a player close to the camera.
        /// </summary>
        public bool UseScreenHeading = true;
        /// <summary>Camera width over height (screen x is 0..1 over the width). config cameraWidth / cameraHeight.</summary>
        public float CameraAspect = 4f / 3f;
        public float ScreenEdgeMargin = 0.02f;

        private const int N = PoseProtocol.JointCount;
        private const int LShoulder = (int)PoseJoint.LeftShoulder;
        private const int RShoulder = (int)PoseJoint.RightShoulder;
        private const int LHip = (int)PoseJoint.LeftHip;
        private const int RHip = (int)PoseJoint.RightHip;

        /// <summary>Parent and child joint of each <see cref="AvatarLimb"/>.</summary>
        public static readonly PoseJoint[] LimbFrom =
        {
            PoseJoint.LeftShoulder, PoseJoint.LeftElbow, PoseJoint.RightShoulder, PoseJoint.RightElbow,
            PoseJoint.LeftHip, PoseJoint.LeftKnee, PoseJoint.RightHip, PoseJoint.RightKnee,
        };
        public static readonly PoseJoint[] LimbTo =
        {
            PoseJoint.LeftElbow, PoseJoint.LeftWrist, PoseJoint.RightElbow, PoseJoint.RightWrist,
            PoseJoint.LeftKnee, PoseJoint.LeftAnkle, PoseJoint.RightKnee, PoseJoint.RightAnkle,
        };

        private readonly AvatarOneEuro[] _filters = new AvatarOneEuro[N];
        private readonly AvatarOneEuro[] _screenFilters = new AvatarOneEuro[N];
        private readonly Vector2[] _screen = new Vector2[N];
        private readonly bool[] _onScreen = new bool[N];
        private readonly Vector3[] _joint = new Vector3[N];
        private readonly float[] _lastSeen = new float[N];
        private readonly Vector3[] _dir = new Vector3[LimbCount];
        private readonly float[] _weight = new float[LimbCount];
        private readonly float[] _thighMax = new float[2];
        private float _torsoMax;
        private Vector3 _torsoUp = Vector3.up;
        private Vector3 _torsoRight = Vector3.right;
        private float _torsoWeight;
        private bool _present;

        public AvatarPoseSolver() => Reset();

        /// <summary>Unit direction of a limb in the avatar frame (last tracked value while it fades out).</summary>
        public Vector3 Direction(AvatarLimb limb) => _dir[(int)limb];

        /// <summary>0 = rest pose, 1 = tracked.</summary>
        public float Weight(AvatarLimb limb) => _weight[(int)limb];

        /// <summary>Hip center to shoulder center, unit, avatar frame.</summary>
        public Vector3 TorsoUp => _torsoUp;
        /// <summary>Left to right shoulder, unit, avatar frame.</summary>
        public Vector3 TorsoRight => _torsoRight;
        public float TorsoWeight => _torsoWeight;
        /// <summary>A person is being tracked (fresh packets with someone in view).</summary>
        public bool Present => _present;

        /// <summary>Smoothed joint in the avatar frame (meters, hip centered).</summary>
        public Vector3 Joint(PoseJoint joint) => _joint[(int)joint];

        public void Reset()
        {
            for (int i = 0; i < N; i++)
            {
                _filters[i].Clear();
                _screenFilters[i].Clear();
                _joint[i] = Vector3.zero;
                _screen[i] = Vector2.zero;
                _onScreen[i] = false;
                _lastSeen[i] = float.NegativeInfinity;
            }

            for (int i = 0; i < LimbCount; i++)
            {
                _dir[i] = Vector3.down;
                _weight[i] = 0f;
            }

            _thighMax[0] = _thighMax[1] = 0f;
            _torsoMax = 0f;
            _torsoUp = Vector3.up;
            _torsoRight = Vector3.right;
            _torsoWeight = 0f;
            _present = false;
        }

        /// <summary>Avatar frame from pose world: the avatar faces away from the camera.</summary>
        public static Vector3 ToAvatar(Vector3 world) => new Vector3(world.x, world.y, -world.z);

        /// <summary>
        /// Call once per rendered frame. <paramref name="frame"/> null or <paramref name="poseAgeSec"/> above
        /// <see cref="LostTimeoutSec"/>: everything returns to rest. <paramref name="now"/> is any monotonic clock.
        /// </summary>
        public void Update(PoseFrame frame, float poseAgeSec, float now, float dt)
        {
            dt = Mathf.Clamp(dt, 0f, MaxFrameDeltaSec);
            bool present = frame != null && frame.HasPerson && poseAgeSec <= LostTimeoutSec;
            if (present && !_present)
                for (int i = 0; i < N; i++)
                {
                    _filters[i].Clear(); // new appearance snaps instead of flying in
                    _screenFilters[i].Clear();
                }
            _present = present;

            if (present)
            {
                for (int i = 0; i < N; i++)
                {
                    if (frame.Visibility[i] < VisibleThreshold) continue; // hold the last value
                    if (!Seen(i, now))
                    {
                        _filters[i].Clear(); // seen again after the hold: start from here, not from a stale value
                        _screenFilters[i].Clear();
                    }
                    _lastSeen[i] = now;
                    _joint[i] = _filters[i].Filter(ToAvatar(frame.World[i]), dt);
                    Vector2 sp = frame.Screen[i];
                    _onScreen[i] = sp.x >= ScreenEdgeMargin && sp.x <= 1f - ScreenEdgeMargin &&
                                   sp.y >= ScreenEdgeMargin && sp.y <= 1f - ScreenEdgeMargin;
                    _screen[i] = _screenFilters[i].Filter(new Vector3(sp.x * CameraAspect, sp.y, 0f), dt);
                }

                UpdateTorso(now, dt);
                for (int i = 0; i < LimbCount; i++) UpdateLimb(i, now, dt);
            }
            else
            {
                _torsoWeight = Mathf.MoveTowards(_torsoWeight, 0f, dt / ReturnSec);
                for (int i = 0; i < LimbCount; i++) _weight[i] = Mathf.MoveTowards(_weight[i], 0f, dt / ReturnSec);
            }
        }

        private float Approach(float weight, bool tracked, float dt) =>
            Mathf.MoveTowards(weight, tracked ? 1f : 0f, dt / (tracked ? AppearSec : ReturnSec));

        private bool Seen(int joint, float now) => now - _lastSeen[joint] <= HoldSec;
        private bool VisibleNow(int joint, float now) => _lastSeen[joint] == now;

        private void UpdateTorso(float now, float dt)
        {
            bool tracked = Seen(LShoulder, now) && Seen(RShoulder, now);
            _torsoWeight = Approach(_torsoWeight, tracked, dt);
            if (!VisibleNow(LShoulder, now) || !VisibleNow(RShoulder, now)) return;

            // Hips may be off frame; MediaPipe still places them, and the world origin is the hip center anyway.
            Vector3 shoulders = (_joint[LShoulder] + _joint[RShoulder]) * 0.5f;
            Vector3 hips = (_joint[LHip] + _joint[RHip]) * 0.5f;
            Vector3 up = shoulders - hips;
            float frontal = new Vector2(up.x, up.y).magnitude;
            _torsoMax = Mathf.Max(_torsoMax * (1f - LengthDecayPerSec * dt), frontal);
            up.z *= TorsoDepthGain;
            Vector3 right = _joint[RShoulder] - _joint[LShoulder];
            right.z *= LimbDepthGain;
            if (up.sqrMagnitude < 1e-6f || right.sqrMagnitude < 1e-6f) return;
            _torsoUp = up.normalized;
            _torsoRight = right.normalized;
        }

        private void UpdateLimb(int limb, float now, float dt)
        {
            int from = (int)LimbFrom[limb];
            int to = (int)LimbTo[limb];
            bool tracked = Seen(from, now) && Seen(to, now);
            _weight[limb] = Approach(_weight[limb], tracked, dt);
            if (!VisibleNow(from, now) || !VisibleNow(to, now)) return;

            Vector3 d = _joint[to] - _joint[from];
            float frontal = new Vector2(d.x, d.y).magnitude;
            float z = d.z * LimbDepthGain;

            var kind = (AvatarLimb)limb;
            bool thigh = kind == AvatarLimb.LeftUpperLeg || kind == AvatarLimb.RightUpperLeg;
            if (thigh)
            {
                int side = kind == AvatarLimb.LeftUpperLeg ? 0 : 1;
                _thighMax[side] = Mathf.Max(_thighMax[side] * (1f - LengthDecayPerSec * dt), frontal);
                float length = Mathf.Max(_thighMax[side], ThighOverTorso * _torsoMax);
                if (length > 1e-4f)
                {
                    float blend = Mathf.Clamp01((ThighRecoverBelow - frontal / length) / ThighRecoverWidth);
                    float rebuilt = Mathf.Sqrt(Mathf.Max(0f, length * length - frontal * frontal)); // forward (squat)
                    z = Mathf.Lerp(z, Mathf.Max(z, rebuilt), blend); // never less forward than measured
                }
            }

            if (UseScreenHeading && _onScreen[from] && _onScreen[to])
            {
                // Heading on the screen plane from the image, length and depth from the world.
                Vector2 img = _screen[to] - _screen[from];
                if (img.sqrMagnitude > 1e-8f)
                {
                    img = img.normalized * frontal;
                    d.x = img.x;
                    d.y = img.y;
                }
            }

            d.z = z;
            if (d.sqrMagnitude < 1e-8f) return;
            _dir[limb] = ClampElevation(d.normalized, MaxForwardDeg, BackwardLimit(kind));
        }

        private float BackwardLimit(AvatarLimb limb)
        {
            switch (limb)
            {
                case AvatarLimb.LeftUpperLeg:
                case AvatarLimb.RightUpperLeg:
                    return MaxBackwardThighDeg;
                case AvatarLimb.LeftLowerLeg:
                case AvatarLimb.RightLowerLeg:
                    return MaxBackwardShinDeg;
                default:
                    return MaxBackwardArmDeg;
            }
        }

        /// <summary>
        /// Clamps the angle of unit <paramref name="d"/> out of the x-y plane to [-backwardDeg, forwardDeg]
        /// (+z forward), keeping its frontal heading.
        /// </summary>
        public static Vector3 ClampElevation(Vector3 d, float forwardDeg, float backwardDeg)
        {
            float elevation = Mathf.Asin(Mathf.Clamp(d.z, -1f, 1f)) * Mathf.Rad2Deg;
            float clamped = Mathf.Clamp(elevation, -backwardDeg, forwardDeg);
            if (Mathf.Approximately(clamped, elevation)) return d;

            var frontal = new Vector2(d.x, d.y);
            frontal = frontal.sqrMagnitude > 1e-10f ? frontal.normalized : Vector2.down;
            float rad = clamped * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            return new Vector3(frontal.x * c, frontal.y * c, Mathf.Sin(rad));
        }
    }
}
