using System.Text;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.UI;

namespace VisionToWall.PoseTest.Tests
{
    /// <summary>Clip definitions, truth CSV format and the comparison math on synthetic data.</summary>
    public sealed class PoseCheckTests
    {
        [Test]
        public void Clips_HaveUniqueNames_AndCoverSpec56Table()
        {
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (PoseClip c in PoseTestClips.All)
            {
                Assert.IsTrue(names.Add(c.Name), c.Name);
                Assert.Greater(c.DurationSec, 1f);
            }

            foreach (string n in new[] { "idle", "raise_left_slow", "raise_left_fast", "raise_right_slow", "raise_right_fast",
                         "wave_right", "squat", "jacks", "walk_out" })
                Assert.IsNotNull(PoseTestClips.Find(n), n);
        }

        [Test]
        public void Raise_GoesFromDownToUpAndBack()
        {
            PoseClip c = PoseTestClips.Find("raise_left_slow");
            Assert.AreEqual(PoseTestClips.ArmDownDeg, c.Evaluate(0f).LeftShoulderAbd, 1e-3f);
            Assert.AreEqual(PoseTestClips.ArmUpDeg, c.Evaluate(3.5f).LeftShoulderAbd, 1e-3f);
            Assert.AreEqual(PoseTestClips.ArmDownDeg, c.Evaluate(6.6f).LeftShoulderAbd, 1e-3f);
            Assert.AreEqual(PoseTestClips.ArmDownDeg, c.Evaluate(3.5f).RightShoulderAbd, 1e-3f, "other arm stays down");
        }

        [Test]
        public void Limb_Directions()
        {
            Assert.Less(Vector3.Angle(PoseRig.Limb(0f, 0f), Vector3.down), 1e-3f);
            Assert.Less(Vector3.Angle(PoseRig.Limb(90f, 0f), Vector3.right), 1e-3f);
            Assert.Less(Vector3.Angle(PoseRig.Limb(-90f, 0f), Vector3.left), 1e-3f);
            Assert.Less(Vector3.Angle(PoseRig.Limb(180f, 0f), Vector3.up), 1e-3f);
            Assert.Less(Vector3.Angle(PoseRig.Limb(0f, 90f), Vector3.forward), 1e-3f);
        }

        [Test]
        public void WalkOut_LeavesTheFrameAndComesBack()
        {
            PoseClip c = PoseTestClips.Find("walk_out");
            Assert.AreEqual(0f, c.Evaluate(0f).RootOffset.x, 1e-3f);
            Assert.Greater(c.Evaluate(5f).RootOffset.x, 2f);
            Assert.AreEqual(0f, c.Evaluate(11f).RootOffset.x, 1e-3f);
        }

        private static float[] Joints(float armUp)
        {
            var j = new float[PoseProtocol.JointCount * PoseProtocol.FloatsPerJoint];
            void Set(PoseJoint joint, float x, float y)
            {
                int o = (int)joint * PoseProtocol.FloatsPerJoint;
                j[o] = 0.5f + x * 0.3f; // mirrored screen x: player's left (negative world x) on the left
                j[o + 1] = 0.5f + y * 0.3f;
                j[o + 2] = x;
                j[o + 3] = y;
                j[o + 4] = 0f;
                j[o + 5] = 1f;
            }

            Set(PoseJoint.Nose, 0f, 0.65f);
            Set(PoseJoint.LeftShoulder, -0.2f, 0.5f);
            Set(PoseJoint.RightShoulder, 0.2f, 0.5f);
            Set(PoseJoint.LeftElbow, -0.25f - 0.25f * armUp, 0.25f + 0.25f * armUp);
            Set(PoseJoint.RightElbow, 0.25f, 0.25f);
            Set(PoseJoint.LeftWrist, -0.3f - 0.5f * armUp, 0.02f + 0.48f * armUp);
            Set(PoseJoint.RightWrist, 0.3f, 0.02f);
            Set(PoseJoint.LeftHip, -0.1f, 0f);
            Set(PoseJoint.RightHip, 0.1f, 0f);
            Set(PoseJoint.LeftKnee, -0.11f, -0.4f);
            Set(PoseJoint.RightKnee, 0.11f, -0.4f);
            Set(PoseJoint.LeftAnkle, -0.12f, -0.8f);
            Set(PoseJoint.RightAnkle, 0.12f, -0.8f);
            return j;
        }

        private static string Csv(int frames, System.Func<int, float[]> joints)
        {
            var sb = new StringBuilder(PoseTruth.Header()).Append('\n');
            var bones = new Quaternion[PoseRig.TruthBones.Length];
            for (int b = 0; b < bones.Length; b++) bones[b] = Quaternion.identity;
            for (int i = 0; i < frames; i++)
            {
                float[] j = joints(i);
                PoseTruth.AppendRow(sb, i * 33, j == null ? 0 : PoseProtocol.JointCount, j, new Vector3(0f, 0.9f, 3f), bones);
            }

            return sb.ToString();
        }

        [Test]
        public void TruthCsv_RoundTrips_AndReadsAsSensorRecording()
        {
            var sb = new StringBuilder(PoseTruth.Header()).Append('\n');
            var bones = new Quaternion[PoseRig.TruthBones.Length];
            for (int b = 0; b < bones.Length; b++) bones[b] = Quaternion.Euler(0f, 0f, b);
            PoseTruth.AppendRow(sb, 0, PoseProtocol.JointCount, Joints(0f), new Vector3(0.1f, 0.9f, 3f), bones);
            PoseTruth.AppendRow(sb, 33, 0, Joints(0f), new Vector3(2.5f, 0.9f, 3f), bones);
            string csv = sb.ToString();

            PoseTruth truth = PoseTruth.Parse(csv);
            Assert.AreEqual(2, truth.Count);
            Assert.IsTrue(truth.Joints[0].HasPerson);
            Assert.IsFalse(truth.Joints[1].HasPerson);
            Assert.AreEqual(2.5f, truth.HipsPosition(1).x, 1e-4f);
            Quaternion q = truth.BoneDelta(0, HumanBodyBones.RightLowerLeg);
            Quaternion e = Quaternion.Euler(0f, 0f, 13f); // 5 decimals in the file; compare components (acos is coarse near 0)
            Assert.AreEqual(e.z, q.z, 1e-4f);
            Assert.AreEqual(e.w, q.w, 1e-4f);
            Assert.AreEqual(0f, q.x, 1e-4f);

            // Same file through the R3 recording parser (what R4 / Dev replays use).
            PoseRecording rec = PoseRecording.Parse(csv);
            Assert.AreEqual(2, rec.Count);
            var f = new PoseFrame();
            rec.CopyTo(0, f, 1);
            Assert.AreEqual(-0.2f, f.World[(int)PoseJoint.LeftShoulder].x, 1e-4f);
        }

        [Test]
        public void TruthCsv_RejectsWrongHeader()
        {
            Assert.Throws<System.FormatException>(() => PoseTruth.Parse("t_ms,count\n0,0"));
        }

        [Test]
        public void IdenticalDetection_Passes_WithZeroError()
        {
            PoseClip clip = PoseTestClips.Find("raise_left_fast");
            string csv = Csv(150, i => Joints(Mathf.Clamp01((i - 15) / 20f)));
            ClipCheck r = PoseCheck.Evaluate(clip, PoseRecording.Parse(csv), PoseRecording.Parse(csv));
            Assert.AreEqual(1f, r.DetectionRate);
            Assert.AreEqual(0f, r.Elbow.Median, 1e-3f);
            Assert.AreEqual(0f, r.Shoulder.Max, 1e-3f);
            Assert.AreEqual(0, r.SwapFrames);
            Assert.Less(r.FigShoulder.Median, 15f);
            Assert.AreEqual(0f, r.OnsetDelayDetected, 1e-3f);
            Assert.Greater(r.OnsetDelayFigure, 0f, "the smoother lags a little");
            Assert.Less(r.OnsetDelayFigure, 0.2f);
            Assert.IsTrue(r.Pass, string.Join("; ", r.Failures));
        }

        [Test]
        public void SwappedLabels_AreCounted()
        {
            PoseClip clip = PoseTestClips.Find("idle");
            string truth = Csv(30, i => Joints(1f));
            string swapped = Csv(30, i =>
            {
                float[] j = Joints(1f);
                for (int k = 1; k < PoseProtocol.JointCount; k += 2) // left/right pairs are (1,2), (3,4), ...
                    for (int f = 0; f < PoseProtocol.FloatsPerJoint; f++)
                    {
                        int a = k * PoseProtocol.FloatsPerJoint + f, b = (k + 1) * PoseProtocol.FloatsPerJoint + f;
                        (j[a], j[b]) = (j[b], j[a]);
                    }
                return j;
            });
            ClipCheck r = PoseCheck.Evaluate(clip, PoseRecording.Parse(truth), PoseRecording.Parse(swapped));
            Assert.AreEqual(30, r.SwapFrames);
            Assert.IsFalse(r.Pass);
        }

        [Test]
        public void MissedFrames_LowerTheDetectionRate()
        {
            PoseClip clip = PoseTestClips.Find("idle");
            ClipCheck r = PoseCheck.Evaluate(clip, PoseRecording.Parse(Csv(100, i => Joints(0f))),
                PoseRecording.Parse(Csv(100, i => i % 5 == 0 ? null : Joints(0f))));
            Assert.AreEqual(0.8f, r.DetectionRate, 1e-4f);
            Assert.IsFalse(r.Pass);
        }

        [Test]
        public void DepthBias_ShowsIn3DButNotIn2D()
        {
            PoseClip clip = PoseTestClips.Find("idle");
            string truth = Csv(30, i => Joints(0f));
            string det = Csv(30, i =>
            {
                float[] j = Joints(0f);
                j[(int)PoseJoint.LeftWrist * PoseProtocol.FloatsPerJoint + 4] = -0.3f; // wrist estimated toward the camera
                return j;
            });
            ClipCheck r = PoseCheck.Evaluate(clip, PoseRecording.Parse(truth), PoseRecording.Parse(det));
            Assert.Greater(r.Elbow.Max, 30f);
            Assert.AreEqual(0f, r.Elbow2D.Max, 1e-3f);
            Assert.AreEqual(-0.15f, r.DepthWrist, 1e-4f);
        }
    }
}
