using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI.Tests
{
    public sealed class PoseSmootherTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>Standing pose in world meters. Torso: shoulders y 0.7, hips y 0.5 → length 0.2.</summary>
        private static PoseFrame Standing(float dx = 0f, float scale = 1f, float vis = 0.99f, bool ankles = false)
        {
            var f = new PoseFrame { HasPerson = true };
            void Set(PoseJoint j, float x, float y)
            {
                f.World[(int)j] = new Vector3(0.5f + dx + (x - 0.5f) * scale, 0.6f + (y - 0.6f) * scale, 0f);
                f.Screen[(int)j] = new Vector2(0f, 0f); // screen is ignored (clamped values must not matter)
                f.Visibility[(int)j] = vis;
            }

            Set(PoseJoint.Nose, 0.5f, 0.85f);
            Set(PoseJoint.LeftShoulder, 0.42f, 0.7f);
            Set(PoseJoint.RightShoulder, 0.58f, 0.7f);
            Set(PoseJoint.LeftElbow, 0.38f, 0.6f);
            Set(PoseJoint.RightElbow, 0.62f, 0.6f);
            Set(PoseJoint.LeftWrist, 0.36f, 0.5f);
            Set(PoseJoint.RightWrist, 0.64f, 0.5f);
            Set(PoseJoint.LeftHip, 0.45f, 0.5f);
            Set(PoseJoint.RightHip, 0.55f, 0.5f);
            Set(PoseJoint.LeftKnee, 0.45f, 0.35f);
            Set(PoseJoint.RightKnee, 0.55f, 0.35f);
            Set(PoseJoint.LeftAnkle, 0.45f, 0.2f);
            Set(PoseJoint.RightAnkle, 0.55f, 0.2f);
            if (!ankles)
            {
                f.Visibility[(int)PoseJoint.LeftAnkle] = 0.1f;
                f.Visibility[(int)PoseJoint.RightAnkle] = 0.1f;
            }

            return f;
        }

        private static PoseSmoother Make() => new PoseSmoother();

        /// <summary>Pushes the frame every tick for <paramref name="seconds"/>. Returns the new time.</summary>
        private static float Run(PoseSmoother s, PoseFrame f, float t, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                t += Dt;
                if (f != null) s.Push(f, t);
                s.Tick(t, Dt);
            }

            return t;
        }

        [Test]
        public void FirstFrame_SnapsToBodyUnits()
        {
            PoseSmoother s = Make();
            s.Push(Standing(), 0f);
            s.Tick(0f, Dt);

            Assert.IsTrue(s.Settled);
            Assert.AreEqual(0.2f, s.Scale, 1e-4f);
            // Center = midpoint of shoulder and hip centers (0.5, 0.6). Shoulders at +0.5 torso, hips at -0.5.
            Assert.AreEqual(0.5f, s.Joint(PoseJoint.LeftShoulder).y, 1e-3f);
            Assert.AreEqual(-0.5f, s.Joint(PoseJoint.RightHip).y, 1e-3f);
            Assert.AreEqual(-0.4f, s.Joint(PoseJoint.LeftShoulder).x, 1e-3f);
            Assert.AreEqual(1f, s.JointAlpha(PoseJoint.LeftWrist), 1e-4f);
        }

        [Test]
        public void SizeIsIndependentOfDistance()
        {
            PoseSmoother near = Make();
            PoseSmoother far = Make();
            near.Push(Standing(scale: 1.5f), 0f);
            near.Tick(0f, Dt);
            far.Push(Standing(scale: 0.6f), 0f);
            far.Tick(0f, Dt);

            for (int j = 0; j < PoseProtocol.JointCount; j++)
            {
                Assert.AreEqual(near.Joint(j).x, far.Joint(j).x, 1e-3f, ((PoseJoint)j).ToString());
                Assert.AreEqual(near.Joint(j).y, far.Joint(j).y, 1e-3f, ((PoseJoint)j).ToString());
            }
        }

        [Test]
        public void Smoothing_ConvergesWithoutJump()
        {
            PoseSmoother s = Make();
            PoseFrame f = Standing();
            float t = Run(s, f, 0f, 0.2f);

            f.World[(int)PoseJoint.RightWrist] = new Vector3(0.64f, 0.9f, 0f); // raise right hand: +2 torso
            s.Push(f, t + Dt);
            s.Tick(t + Dt, Dt);
            float y1 = s.Joint(PoseJoint.RightWrist).y;
            Assert.Greater(y1, -0.5f);
            Assert.Less(y1, 1.5f, "one frame must not jump all the way");

            Run(s, f, t + Dt, 0.5f);
            Assert.AreEqual(1.5f, s.Joint(PoseJoint.RightWrist).y, 0.02f);
        }

        [Test]
        public void LowVisibility_HoldsThenDims()
        {
            PoseSmoother s = Make();
            PoseFrame f = Standing();
            float t = Run(s, f, 0f, 0.2f);
            Vector2 held = s.Joint(PoseJoint.LeftWrist);

            f.Visibility[(int)PoseJoint.LeftWrist] = 0.2f;
            f.World[(int)PoseJoint.LeftWrist] = new Vector3(0.1f, 0.6f, 0f); // low-confidence estimate
            t = Run(s, f, t, 0.25f);
            Assert.AreEqual(held.x, s.Joint(PoseJoint.LeftWrist).x, 0.02f, "position held");
            Assert.AreEqual(held.y, s.Joint(PoseJoint.LeftWrist).y, 0.02f, "position held");
            Assert.AreEqual(1f, s.JointAlpha(PoseJoint.LeftWrist), 1e-4f, "still bright inside the hold");

            Run(s, f, t, 0.6f);
            Assert.AreEqual(s.DimAlpha, s.JointAlpha(PoseJoint.LeftWrist), 1e-4f);
            Assert.AreEqual(s.DimAlpha, s.BoneAlpha(PoseJoint.LeftElbow, PoseJoint.LeftWrist), 1e-4f, "bone dims too");
            Assert.AreEqual(1f, s.BoneAlpha(PoseJoint.LeftShoulder, PoseJoint.LeftElbow), 1e-4f);
            Assert.Less(s.Joint(PoseJoint.LeftWrist).x, held.x - 0.2f, "after the hold the dimmed joint follows the estimate");
        }

        [Test]
        public void JointNeverSeen_StartsDim()
        {
            PoseSmoother s = Make();
            s.Push(Standing(), 0f);
            s.Tick(0f, Dt);
            Assert.AreEqual(s.DimAlpha, s.JointAlpha(PoseJoint.LeftAnkle), 1e-4f);
        }

        [Test]
        public void CountZero_FadesOutThenSnapsOnReturn()
        {
            PoseSmoother s = Make();
            float t = Run(s, Standing(), 0f, 0.5f);
            Assert.AreEqual(1f, s.Presence, 1e-4f);

            s.Push(new PoseFrame { HasPerson = false }, t);
            t = Run(s, null, t, 0.5f);
            Assert.AreEqual(0f, s.Presence, 1e-4f);
            Assert.IsFalse(s.Settled);

            // Comes back 0.3 screen widths to the right: no fly-in, snaps to the same body-unit pose.
            PoseFrame back = Standing(dx: 0.3f);
            s.Push(back, t + Dt);
            s.Tick(t + Dt, Dt);
            Assert.AreEqual(0.8f, s.Center.x, 1e-3f);
            Assert.AreEqual(-0.4f, s.Joint(PoseJoint.LeftShoulder).x, 1e-3f);
        }

        [Test]
        public void Silence_FadesOutAfterTimeout()
        {
            PoseSmoother s = Make();
            float t = Run(s, Standing(), 0f, 0.5f);
            t = Run(s, null, t, 0.4f);
            Assert.AreEqual(1f, s.Presence, 1e-4f, "short gap (Pose skip frames) keeps the figure");

            Run(s, null, t, 0.5f);
            Assert.AreEqual(0f, s.Presence, 1e-4f);
        }

        [Test]
        public void NoShoulders_DoesNotSettle()
        {
            PoseSmoother s = Make();
            PoseFrame f = Standing();
            f.Visibility[(int)PoseJoint.LeftShoulder] = 0.1f;
            Run(s, f, 0f, 0.5f);
            Assert.IsFalse(s.Settled);
            Assert.AreEqual(0f, s.Presence);
        }

        [Test]
        public void UpperBodyOnly_SettlesWithFullFigure()
        {
            // Desk test / close to the camera: hips and legs out of frame, but the world pose is still estimated.
            PoseSmoother s = Make();
            PoseFrame f = Standing();
            for (int j = (int)PoseJoint.LeftHip; j <= (int)PoseJoint.RightAnkle; j++) f.Visibility[j] = 0.1f;
            Run(s, f, 0f, 0.5f);

            Assert.IsTrue(s.Settled);
            Assert.AreEqual(1f, s.Presence, 1e-4f);
            Assert.AreEqual(0.5f, s.Joint(PoseJoint.LeftShoulder).y, 1e-3f);
            Assert.AreEqual(-0.5f, s.Joint(PoseJoint.LeftHip).y, 1e-3f, "hip estimate kept in proportion");
            Assert.AreEqual(s.DimAlpha, s.JointAlpha(PoseJoint.LeftKnee), 1e-4f, "out-of-frame joints are dim");
            Assert.AreEqual(1f, s.JointAlpha(PoseJoint.RightWrist), 1e-4f);
        }

        [Test]
        public void BadTorsoLength_Ignored()
        {
            PoseSmoother s = Make();
            Run(s, Standing(scale: 0.2f), 0f, 0.5f); // 4 cm torso
            Assert.IsFalse(s.Settled);
        }

        [Test]
        public void Squat_WithAnkles_LowersBody()
        {
            PoseSmoother s = Make();
            float t = Run(s, Standing(ankles: true), 0f, 1f);
            float shoulderStand = s.Joint(PoseJoint.LeftShoulder).y;
            float ankleStand = s.Joint(PoseJoint.LeftAnkle).y;

            // Squat: everything above the ankles drops by 0.12 screen heights.
            PoseFrame squat = Standing(ankles: true);
            for (int j = 0; j < (int)PoseJoint.LeftAnkle; j++) squat.World[j].y -= 0.12f;
            Run(s, squat, t, 3f);

            Assert.AreEqual(ankleStand, s.Joint(PoseJoint.LeftAnkle).y, 0.05f, "feet stay on the base");
            Assert.Less(s.Joint(PoseJoint.LeftShoulder).y, shoulderStand - 0.4f, "body lowered");
        }

        [Test]
        public void NonFiniteJoint_TreatedAsInvisible()
        {
            PoseSmoother s = Make();
            PoseFrame f = Standing();
            float t = Run(s, f, 0f, 0.2f);
            Vector2 before = s.Joint(PoseJoint.RightElbow);
            f.World[(int)PoseJoint.RightElbow] = new Vector3(float.NaN, 0.5f, 0f);
            Run(s, f, t, 0.1f);
            Assert.AreEqual(before, s.Joint(PoseJoint.RightElbow));
        }

        /// <summary>
        /// Player promise 3 (no lag): the Unity side adds at most about 0.1 s before the figure has moved 80 % of a
        /// sudden pose change. Sensor and network latency come on top (measured separately).
        /// </summary>
        [Test]
        public void StepResponse_Reaches80PercentWithin100ms()
        {
            PoseSmoother s = Make();
            PoseFrame f = Standing();
            float t = Run(s, f, 0f, 1f);
            float start = s.Joint(PoseJoint.RightWrist).y;

            f.World[(int)PoseJoint.RightWrist] = new Vector3(0.64f, 0.9f, 0f); // +2 torso
            float target = 1.5f;
            float elapsed = 0f;
            while (elapsed < 1f)
            {
                t += Dt;
                elapsed += Dt;
                s.Push(f, t);
                s.Tick(t, Dt);
                if (s.Joint(PoseJoint.RightWrist).y - start >= 0.8f * (target - start)) break;
            }

            Assert.LessOrEqual(elapsed, 0.1f + 1e-4f, $"80 % after {elapsed:0.000} s");
        }

        [Test]
        public void Tick_ClampsLongFrame()
        {
            PoseSmoother s = Make();
            float t = Run(s, Standing(), 0f, 0.5f);
            s.Push(new PoseFrame { HasPerson = false }, t);
            s.Tick(t + 3.8f, 3.8f); // Play-entry spike must not finish the 0.3 s fade at once
            Assert.Greater(s.Presence, 0.5f);
        }
    }
}
