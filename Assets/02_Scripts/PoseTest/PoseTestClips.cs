using System;
using UnityEngine;

namespace VisionToWall.PoseTest
{
    /// <summary>
    /// Code pose: joint angles in degrees (no animation clip). Directions are in the avatar's own frame
    /// (avatar facing +z, its left side at -x, +y up) so they do not depend on where the avatar stands.
    /// <list type="bullet">
    /// <item>Shoulder abduction: 0 = arm straight down, 90 = sideways (T-pose), 180 = straight up (frontal plane).</item>
    /// <item>Shoulder flexion: arm swung forward (sagittal plane), applied before abduction. Relative to the torso.</item>
    /// <item>Elbow flexion: 0 = straight; the forearm turns further in the frontal plane (up when the arm is sideways).
    /// Interior elbow angle = 180 - flexion.</item>
    /// <item>Hip abduction: thigh sideways. Hip flexion: thigh forward. Knee flexion: shank backward.
    /// Interior knee angle = 180 - flexion.</item>
    /// <item>Torso lean: whole upper body tilted forward around the hips.</item>
    /// </list>
    /// Feet are kept on the floor by the rig (pelvis drops in a squat).
    /// </summary>
    [Serializable]
    public struct PoseSpec
    {
        public float LeftShoulderAbd, RightShoulderAbd;
        public float LeftShoulderFlex, RightShoulderFlex;
        public float LeftElbowFlex, RightElbowFlex;
        public float LeftHipAbd, RightHipAbd;
        public float LeftHipFlex, RightHipFlex;
        public float LeftKneeFlex, RightKneeFlex;
        public float TorsoLean;
        /// <summary>Whole-body offset in meters, camera axes (+x camera right, +y up, +z away from the camera).</summary>
        public Vector3 RootOffset;

        /// <summary>Arms down (10 deg: 0 would push the hands through the thighs), elbows slightly bent, legs straight.</summary>
        public static PoseSpec Rest => new PoseSpec
        {
            LeftShoulderAbd = PoseTestClips.ArmDownDeg,
            RightShoulderAbd = PoseTestClips.ArmDownDeg,
            LeftElbowFlex = 8f,
            RightElbowFlex = 8f,
        };
    }

    public enum ActiveArm
    {
        None,
        Left,
        Right,
        Both
    }

    public sealed class PoseClip
    {
        public readonly string Name;
        public readonly string Description;
        public readonly float DurationSec;
        /// <summary>Arm whose raise is timed for the latency check (shoulder angle onset).</summary>
        public readonly ActiveArm Arm;
        /// <summary>True for the clip that walks out of the frame (appear / disappear check).</summary>
        public readonly bool LeavesFrame;
        /// <summary>True when the knees bend enough for the knee angle to be judged.</summary>
        public readonly bool UsesKnees;
        private readonly Func<float, PoseSpec> _evaluate;

        public PoseClip(string name, string description, float durationSec, ActiveArm arm, bool leavesFrame,
            bool usesKnees, Func<float, PoseSpec> evaluate)
        {
            Name = name;
            Description = description;
            DurationSec = durationSec;
            Arm = arm;
            LeavesFrame = leavesFrame;
            UsesKnees = usesKnees;
            _evaluate = evaluate;
        }

        public int FrameCount(float fps) => Mathf.FloorToInt(DurationSec * fps) + 1;

        public PoseSpec Evaluate(float t) => _evaluate(Mathf.Clamp(t, 0f, DurationSec));
    }

    /// <summary>
    /// The pose sequences of spec 56 2절. Pure functions of time; the rig turns them into bone rotations.
    /// </summary>
    public static class PoseTestClips
    {
        public const float Fps = 30f;
        public const float ArmDownDeg = 10f;
        public const float ArmUpDeg = 180f;

        public static readonly PoseClip[] All =
        {
            new PoseClip("idle", "가만히 서 있기: 팔 내림 + 호흡 흔들림 (어깨 ±1.5°, 몸 ±1 cm)", 5f, ActiveArm.None, false, false, Idle),
            new PoseClip("raise_left_slow", "왼팔 들기 느리게: 10° → 180° (3초) → 10° (3초)", 7f, ActiveArm.Left, false, false,
                t => Raise(t, true, 3f, 0f, 1)),
            new PoseClip("raise_left_fast", "왼팔 들기 빠르게: 10° → 180° (0.6초) → 유지 0.4초 → 10° (0.6초), 2회", 5f, ActiveArm.Left, false, false,
                t => Raise(t, true, 0.6f, 0.4f, 2)),
            new PoseClip("raise_right_slow", "오른팔 들기 느리게: 10° → 180° (3초) → 10° (3초)", 7f, ActiveArm.Right, false, false,
                t => Raise(t, false, 3f, 0f, 1)),
            new PoseClip("raise_right_fast", "오른팔 들기 빠르게: 10° → 180° (0.6초) → 유지 0.4초 → 10° (0.6초), 2회", 5f, ActiveArm.Right, false, false,
                t => Raise(t, false, 0.6f, 0.4f, 2)),
            new PoseClip("wave_right", "오른팔 흔들기: 상완 100° 들고 팔꿈치 80° 굽힘, 전완 ±35° 1.5 Hz 4초", 7f, ActiveArm.Right, false, false, WaveRight),
            new PoseClip("squat", "쪼그려 앉기 2회: 고관절 굽힘 85°, 무릎 굽힘 100° (무릎 안쪽 80°), 상체 숙임 40°, 팔 앞으로 70°", 9f, ActiveArm.None, false, true, Squat),
            new PoseClip("jacks", "벌려 뛰기 4회: 팔 10° ↔ 160°, 다리 0° ↔ 22° (1.2초 주기)", 6.5f, ActiveArm.Both, false, false, Jacks),
            new PoseClip("walk_out", "옆으로 걸어 나갔다 돌아오기: 0.8 m/s로 +x 2.4 m (화면 밖) → 2초 대기 → 복귀", 12f, ActiveArm.None, true, false, WalkOut),
        };

        public static PoseClip Find(string name)
        {
            foreach (PoseClip c in All)
                if (c.Name == name) return c;
            return null;
        }

        /// <summary>0..1 ease in and out.</summary>
        public static float Ease(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        private static PoseSpec Idle(float t)
        {
            PoseSpec p = PoseSpec.Rest;
            float breathe = Mathf.Sin(2f * Mathf.PI * 0.25f * t);
            p.LeftShoulderAbd += 1.5f * breathe;
            p.RightShoulderAbd += 1.5f * breathe;
            p.TorsoLean = 1f * breathe;
            p.RootOffset = new Vector3(0.01f * Mathf.Sin(2f * Mathf.PI * 0.2f * t), 0f, 0f);
            return p;
        }

        /// <summary>0.5 s rest, then <paramref name="reps"/> x (up, hold, down), then rest.</summary>
        private static PoseSpec Raise(float t, bool left, float moveSec, float holdSec, int reps)
        {
            PoseSpec p = PoseSpec.Rest;
            float u = 0f;
            float local = t - 0.5f;
            float cycle = moveSec * 2f + holdSec + 0.4f;
            if (local >= 0f && local < cycle * reps)
            {
                float c = local % cycle;
                if (c < moveSec) u = Ease(c / moveSec);
                else if (c < moveSec + holdSec) u = 1f;
                else if (c < moveSec * 2f + holdSec) u = 1f - Ease((c - moveSec - holdSec) / moveSec);
            }

            float abd = Mathf.Lerp(ArmDownDeg, ArmUpDeg, u);
            if (left) p.LeftShoulderAbd = abd;
            else p.RightShoulderAbd = abd;
            return p;
        }

        private static PoseSpec WaveRight(float t)
        {
            PoseSpec p = PoseSpec.Rest;
            float up = t < 0.5f ? 0f : t < 1.5f ? Ease(t - 0.5f) : t < 5.5f ? 1f : 1f - Ease(t - 5.5f);
            float swing = t >= 1.5f && t < 5.5f ? 35f * Mathf.Sin(2f * Mathf.PI * 1.5f * (t - 1.5f)) : 0f;
            p.RightShoulderAbd = Mathf.Lerp(ArmDownDeg, 100f, up);
            p.RightElbowFlex = Mathf.Lerp(8f, 80f, up) + swing;
            return p;
        }

        private static PoseSpec Squat(float t)
        {
            // 1 s stand, then 2 x (1.5 s down, 0.5 s hold, 1.5 s up, 0.5 s hold).
            float u = 0f;
            float local = t - 1f;
            if (local >= 0f && local < 8f)
            {
                float c = local % 4f;
                if (c < 1.5f) u = Ease(c / 1.5f);
                else if (c < 2f) u = 1f;
                else if (c < 3.5f) u = 1f - Ease((c - 2f) / 1.5f);
            }

            PoseSpec p = PoseSpec.Rest;
            p.LeftHipFlex = p.RightHipFlex = 85f * u;
            p.LeftKneeFlex = p.RightKneeFlex = 100f * u;
            p.LeftHipAbd = p.RightHipAbd = 8f * u;
            p.TorsoLean = 40f * u;
            p.LeftShoulderFlex = p.RightShoulderFlex = 70f * u;
            p.LeftShoulderAbd = p.RightShoulderAbd = Mathf.Lerp(ArmDownDeg, 0f, u);
            return p;
        }

        private static PoseSpec Jacks(float t)
        {
            // 1 s stand, 4 x 1.2 s (open 0.6, close 0.6), then stand.
            float u = 0f;
            float local = t - 1f;
            if (local >= 0f && local < 4.8f)
            {
                float c = local % 1.2f;
                u = c < 0.6f ? Ease(c / 0.6f) : 1f - Ease((c - 0.6f) / 0.6f);
            }

            PoseSpec p = PoseSpec.Rest;
            p.LeftShoulderAbd = p.RightShoulderAbd = Mathf.Lerp(ArmDownDeg, 160f, u);
            p.LeftHipAbd = p.RightHipAbd = 22f * u;
            return p;
        }

        private static PoseSpec WalkOut(float t)
        {
            // 1 s still, 3 s out (+x, 0.8 m/s), 2 s out of frame, 3 s back, 3 s still.
            const float speed = 0.8f;
            float x;
            bool walking;
            if (t < 1f) { x = 0f; walking = false; }
            else if (t < 4f) { x = speed * (t - 1f); walking = true; }
            else if (t < 6f) { x = speed * 3f; walking = false; }
            else if (t < 9f) { x = speed * (9f - t); walking = true; }
            else { x = 0f; walking = false; }

            PoseSpec p = PoseSpec.Rest;
            p.RootOffset = new Vector3(x, 0f, 0f);
            if (walking)
            {
                float s = Mathf.Sin(2f * Mathf.PI * 1.6f * (t - 1f));
                p.LeftHipAbd = 12f * Mathf.Max(0f, s);
                p.RightHipAbd = 12f * Mathf.Max(0f, -s);
                p.LeftKneeFlex = 15f * Mathf.Max(0f, s);
                p.RightKneeFlex = 15f * Mathf.Max(0f, -s);
                p.LeftShoulderAbd += 4f * Mathf.Max(0f, s);
                p.RightShoulderAbd += 4f * Mathf.Max(0f, -s);
            }

            return p;
        }
    }
}
