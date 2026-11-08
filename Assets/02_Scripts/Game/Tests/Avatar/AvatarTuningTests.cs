using System.IO;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.Game.Dev;
using VisionToWall.UI;

namespace VisionToWall.Game.Tests
{
    /// <summary>
    /// R4b (doc 57): image heading, faster comeback of a limb seen again, and the user comparison recording
    /// (<c>Data/user_compare_joints.csv</c>, joints only, from <c>pose_compare_record.py</c>) replayed before / after.
    /// </summary>
    public sealed class AvatarTuningTests
    {
        private const float Dt = 1f / 60f;

        private static string DataDir => Path.Combine(Application.dataPath, "02_Scripts/Game/Tests/Avatar/Data");

        private static PoseFrame ArmFrame(Vector2 shoulderScreen, Vector2 elbowScreen, Vector3 shoulderWorld, Vector3 elbowWorld)
        {
            var f = new PoseFrame { HasPerson = true };
            for (int j = 0; j < PoseProtocol.JointCount; j++)
            {
                f.Screen[j] = new Vector2(0.5f, 0.5f);
                f.Visibility[j] = 1f;
            }
            f.Screen[(int)PoseJoint.RightShoulder] = shoulderScreen;
            f.Screen[(int)PoseJoint.RightElbow] = elbowScreen;
            f.World[(int)PoseJoint.RightShoulder] = shoulderWorld;
            f.World[(int)PoseJoint.RightElbow] = elbowWorld;
            f.World[(int)PoseJoint.LeftShoulder] = new Vector3(-0.18f, 0.5f, 0f);
            f.Screen[(int)PoseJoint.LeftShoulder] = new Vector2(0.4f, 0.6f);
            return f;
        }

        private static void Run(AvatarPoseSolver s, PoseFrame f, ref float now, float sec)
        {
            for (float t = 0f; t < sec; t += Dt)
            {
                now += Dt;
                s.Update(f, 0f, now, Dt);
            }
        }

        [Test]
        public void ScreenHeading_FollowsTheImage_KeepsWorldLengthAndDepth()
        {
            // World says the upper arm hangs straight down; the image shows it 45 degrees out to the right.
            PoseFrame f = ArmFrame(new Vector2(0.6f, 0.6f), new Vector2(0.6f + 0.1f / (4f / 3f), 0.5f),
                new Vector3(0.18f, 0.5f, 0f), new Vector3(0.18f, 0.22f, -0.1f));
            var s = new AvatarPoseSolver();
            float now = 0f;
            Run(s, f, ref now, 1f);
            Vector3 d = s.Direction(AvatarLimb.RightUpperArm);
            Assert.That(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, Is.EqualTo(-45f).Within(0.5f), "heading from the image");
            Vector3 world = AvatarPoseSolver.ToAvatar(new Vector3(0f, -0.28f, -0.1f));
            world.z *= s.LimbDepthGain;
            Assert.That(d.z, Is.EqualTo(world.normalized.z).Within(1e-3f), "depth share from the world");

            var off = new AvatarPoseSolver { UseScreenHeading = false };
            now = 0f;
            Run(off, f, ref now, 1f);
            Assert.That(off.Direction(AvatarLimb.RightUpperArm).x, Is.EqualTo(0f).Within(1e-3f), "world heading when off");
        }

        [Test]
        public void ScreenHeading_NotUsedAtTheImageEdge()
        {
            // Elbow clamped at the bottom edge (off frame): the 2D direction is not real, keep the world.
            PoseFrame f = ArmFrame(new Vector2(0.6f, 0.1f), new Vector2(0.9f, 0f),
                new Vector3(0.18f, 0.5f, 0f), new Vector3(0.18f, 0.22f, 0f));
            var s = new AvatarPoseSolver();
            float now = 0f;
            Run(s, f, ref now, 1f);
            Assert.That(s.Direction(AvatarLimb.RightUpperArm).y, Is.LessThan(-0.99f));
        }

        [Test]
        public void LimbSeenAgain_ComesBackFast_ButLeavesSlowly()
        {
            PoseFrame f = ArmFrame(new Vector2(0.6f, 0.6f), new Vector2(0.75f, 0.6f),
                new Vector3(0.18f, 0.5f, 0f), new Vector3(0.46f, 0.5f, 0f));
            var s = new AvatarPoseSolver();
            float now = 0f;
            Run(s, f, ref now, 0.5f);
            f.Visibility[(int)PoseJoint.RightElbow] = 0.1f;
            Run(s, f, ref now, AvatarPoseSolver.HoldSec + AvatarPoseSolver.ReturnSec * 0.5f);
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.InRange(0.3f, 0.7f), "leaves over ReturnSec");
            Run(s, f, ref now, AvatarPoseSolver.ReturnSec);
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.EqualTo(0f));

            // Seen again with the arm somewhere else: tracked again within AppearSec, already pointing there.
            f.Visibility[(int)PoseJoint.RightElbow] = 1f;
            f.World[(int)PoseJoint.RightElbow] = new Vector3(0.18f, 0.78f, 0f); // straight up
            f.Screen[(int)PoseJoint.RightElbow] = new Vector2(0.6f, 0.8f);
            Run(s, f, ref now, s.AppearSec + 0.02f);
            Assert.That(s.Weight(AvatarLimb.RightUpperArm), Is.EqualTo(1f));
            Assert.That(s.Direction(AvatarLimb.RightUpperArm).y, Is.GreaterThan(0.95f), "no fly-in from the old pose");
        }

        [Test]
        public void UserRecording_R4b_MatchesTheCameraBetterThanR4()
        {
            PoseRecording rec = PoseRecording.Parse(File.ReadAllText(Path.Combine(DataDir, "user_compare_joints.csv")));
            using (var rig = new AvatarCompare.Rig())
            {
                var r4 = AvatarCompare.Summary(AvatarCompare.Run(rec, new AvatarPoseSolver
                {
                    UseScreenHeading = false,
                    AppearSec = AvatarPoseSolver.ReturnSec,
                }, rig));
                var r4b = AvatarCompare.Summary(AvatarCompare.Run(rec, new AvatarPoseSolver(), rig));
                Debug.Log($"[R4b] user recording on-frame limb angle vs camera: R4 median {r4.median:F1} p95 {r4.p95:F1} over25 {r4.over25:P1} -> R4b median {r4b.median:F1} p95 {r4b.p95:F1} over25 {r4b.over25:P1}; visible limbs at the image edge {r4b.offFrame:P1}");
                Assert.That(r4b.median, Is.LessThan(r4.median));
                Assert.That(r4b.over25, Is.LessThan(r4.over25));
                Assert.That(r4b.over25, Is.LessThan(0.03f));
            }
        }
    }
}
