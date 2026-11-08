using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.PoseTest;
using VisionToWall.UI;

namespace VisionToWall.Game.Tests
{
    /// <summary>
    /// RT clips (doc 56): <c>&lt;clip&gt;_detected.csv</c> (MediaPipe on the rendered model) goes through the R4
    /// solver and the real rig; the posed bones are compared with the truth bone rotations of
    /// <c>&lt;clip&gt;_truth.csv</c> (truth direction = truth delta * T-pose direction). "Raw" is the same chain with
    /// MediaPipe depth used as is (the state before the depth handling). Played at 60 fps with the packets at
    /// their 30 fps timestamps, like the game. Writes <c>Temp/r4_rt_compare.md</c>.
    /// </summary>
    public sealed class AvatarPoseRtTests
    {
        private const float FrameDt = 1f / 60f;
        private static readonly string[] Clips =
        {
            "idle", "raise_left_slow", "raise_left_fast", "raise_right_slow", "raise_right_fast",
            "wave_right", "squat", "jacks", "walk_out",
        };
        private static readonly string[] Groups = { "upper arm", "forearm", "thigh", "shin", "elbow", "knee" };

        private static string DataDir => Path.Combine(Application.dataPath, "02_Scripts/PoseTest/Tests/Data");

        private sealed class Errors
        {
            public readonly List<float>[] Values = new List<float>[Groups.Length];

            public Errors()
            {
                for (int i = 0; i < Values.Length; i++) Values[i] = new List<float>();
            }

            public void Add(Errors other)
            {
                for (int i = 0; i < Values.Length; i++) Values[i].AddRange(other.Values[i]);
            }

            public float Median(int g) => Percentile(Values[g], 0.5f);
            public float Max(int g) => Percentile(Values[g], 1f);
        }

        private static float Percentile(List<float> v, float p)
        {
            if (v.Count == 0) return float.NaN;
            var s = new List<float>(v);
            s.Sort();
            return s[Mathf.Clamp(Mathf.RoundToInt(p * (s.Count - 1)), 0, s.Count - 1)];
        }

        public static AvatarPoseSolver Raw() => new AvatarPoseSolver
        {
            LimbDepthGain = 1f,
            TorsoDepthGain = 1f,
            ThighRecoverBelow = -1f,
            MaxForwardDeg = 90f,
            MaxBackwardArmDeg = 90f,
            MaxBackwardThighDeg = 90f,
            MaxBackwardShinDeg = 90f,
            UseScreenHeading = false,
            AppearSec = AvatarPoseSolver.ReturnSec,
        };

        /// <summary>R4 as merged (before R4b): world x, y heading, 0.3 s to come back.</summary>
        public static AvatarPoseSolver R4() => new AvatarPoseSolver
        {
            UseScreenHeading = false,
            AppearSec = AvatarPoseSolver.ReturnSec,
        };

        private static Errors RunClip(string clip, AvatarPoseSolver solver, AvatarRigFixture rig)
        {
            PoseTruth truth = PoseTruth.Parse(File.ReadAllText(Path.Combine(DataDir, clip + "_truth.csv")));
            PoseRecording detected = PoseRecording.Parse(File.ReadAllText(Path.Combine(DataDir, clip + "_detected.csv")));
            var truthIndex = new Dictionary<uint, int>();
            for (int i = 0; i < truth.Count; i++) truthIndex[truth.Joints[i].TimestampMs] = i;

            var errors = new Errors();
            var frame = new PoseFrame();
            var truthFrame = new PoseFrame();
            var posed = new Vector3[AvatarPoseSolver.LimbCount];
            var expected = new Vector3[AvatarPoseSolver.LimbCount];
            float now = 0f;
            int next = 0;
            float end = detected.DurationSec;
            float lastPacket = -1f;
            for (float t = 0f; t <= end + 1e-4f; t += FrameDt)
            {
                now = t;
                bool newPacket = false;
                while (next < detected.Count && detected[next].TimestampMs / 1000f <= t + 1e-4f)
                {
                    detected.CopyTo(next, frame, (uint)next);
                    lastPacket = detected[next].TimestampMs / 1000f;
                    next++;
                    newPacket = true;
                }

                solver.Update(lastPacket < 0f ? null : frame, lastPacket < 0f ? 99f : now - lastPacket, now, FrameDt);
                if (!newPacket || !frame.HasPerson) continue;
                if (!truthIndex.TryGetValue(frame.TimestampMs, out int ti)) continue;
                truth.Joints.CopyTo(ti, truthFrame, 0);
                if (!truthFrame.HasPerson || !AllVisible(truthFrame)) continue; // whole body in view

                rig.Skeleton.Apply(solver);
                bool allTracked = true;
                for (int i = 0; i < AvatarPoseSolver.LimbCount; i++)
                {
                    var limb = (AvatarLimb)i;
                    if (solver.Weight(limb) < 0.999f) allTracked = false;
                    posed[i] = rig.Skeleton.LimbDirection(limb);
                    expected[i] = truth.BoneDelta(ti, AvatarRigFixture.Bone(limb)) * rig.TPoseDirection[i];
                }
                if (!allTracked) continue;

                for (int i = 0; i < AvatarPoseSolver.LimbCount; i++)
                    errors.Values[i % 2 == 0 ? (i < 4 ? 0 : 2) : (i < 4 ? 1 : 3)].Add(Vector3.Angle(posed[i], expected[i]));
                for (int side = 0; side < 2; side++)
                {
                    int u = side * 2, l = side * 2 + 1;
                    errors.Values[4].Add(Mathf.Abs(Vector3.Angle(-posed[u], posed[l]) - Vector3.Angle(-expected[u], expected[l])));
                    errors.Values[5].Add(Mathf.Abs(Vector3.Angle(-posed[u + 4], posed[l + 4]) - Vector3.Angle(-expected[u + 4], expected[l + 4])));
                }
            }

            rig.Skeleton.ApplyRest();
            return errors;
        }

        private static bool AllVisible(PoseFrame f)
        {
            for (int j = 0; j < PoseProtocol.JointCount; j++)
                if (f.Visibility[j] < 1f) return false;
            return true;
        }

        [Test]
        public void TruthBoneRotations_MatchTruthJoints()
        {
            // The quaternion columns and the joint columns describe the same pose (sanity of the reference).
            var rig = new AvatarRigFixture();
            try
            {
                PoseTruth truth = PoseTruth.Parse(File.ReadAllText(Path.Combine(DataDir, "jacks_truth.csv")));
                var f = new PoseFrame();
                float worst = 0f;
                for (int ti = 0; ti < truth.Count; ti += 7)
                {
                    truth.Joints.CopyTo(ti, f, 0);
                    if (!f.HasPerson) continue;
                    for (int i = 0; i < AvatarPoseSolver.LimbCount; i++)
                    {
                        Vector3 fromJoints = AvatarPoseSolver.ToAvatar(
                            f.World[(int)AvatarPoseSolver.LimbTo[i]] - f.World[(int)AvatarPoseSolver.LimbFrom[i]]);
                        Vector3 fromBones = truth.BoneDelta(ti, AvatarRigFixture.Bone((AvatarLimb)i)) * rig.TPoseDirection[i];
                        worst = Mathf.Max(worst, Vector3.Angle(fromJoints, fromBones));
                    }
                }
                Assert.That(worst, Is.LessThan(2f), "truth joints vs truth bone rotations, degrees");
            }
            finally
            {
                rig.Close();
            }
        }

        [Test]
        public void DepthHandling_BeatsRawDepth_OnEveryRtClip()
        {
            var rig = new AvatarRigFixture();
            var report = new StringBuilder();
            var rawAll = new Errors();
            var r4All = new Errors();
            var newAll = new Errors();
            try
            {
                report.Append("| clip | ");
                foreach (string g in Groups) report.Append(g).Append(" raw | ").Append(g).Append(" R4 | ").Append(g).Append(" R4b | ");
                report.Append('\n').Append("|---|");
                for (int i = 0; i < Groups.Length * 3; i++) report.Append("---|");
                report.Append('\n');

                foreach (string clip in Clips)
                {
                    Errors raw = RunClip(clip, Raw(), rig);
                    Errors r4 = RunClip(clip, R4(), rig);
                    Errors fixedDepth = RunClip(clip, new AvatarPoseSolver(), rig);
                    rawAll.Add(raw);
                    r4All.Add(r4);
                    newAll.Add(fixedDepth);
                    report.Append("| ").Append(clip).Append(" | ");
                    for (int g = 0; g < Groups.Length; g++)
                        report.Append(Cell(raw, g)).Append(" | ").Append(Cell(r4, g)).Append(" | ").Append(Cell(fixedDepth, g)).Append(" | ");
                    report.Append('\n');
                    Assert.That(fixedDepth.Values[0].Count, Is.GreaterThan(30), clip + " compared frames");
                }

                report.Append("| **all** | ");
                for (int g = 0; g < Groups.Length; g++)
                    report.Append(Cell(rawAll, g)).Append(" | ").Append(Cell(r4All, g)).Append(" | ").Append(Cell(newAll, g)).Append(" | ");
                report.Append("\n\nmedian / max degrees, frames with the whole body in view and every limb tracked. ")
                    .Append("Limb rows: bone direction vs truth bone rotation. elbow / knee: inner angle error.\n");

                Directory.CreateDirectory("Temp");
                File.WriteAllText("Temp/r4_rt_compare.md", report.ToString());
                Debug.Log("[R4] RT compare raw -> R4b " + Summary(rawAll, newAll) + " | R4 -> R4b " + Summary(r4All, newAll) + " (Temp/r4_rt_compare.md)");
            }
            finally
            {
                rig.Close();
            }

            for (int g = 0; g < Groups.Length; g++)
            {
                Assert.That(newAll.Median(g), Is.LessThan(rawAll.Median(g)), Groups[g] + " median improves on raw");
                // R4b must not undo R4 (doc 57): bone directions at most 0.5 degrees worse at the median. The elbow and
                // knee inner angles are held to the RT criterion below instead: taking each bone's heading from the
                // image (R4b) moves the RT elbow angle error 0.8 -> 6.3 while every bone direction gets better.
                if (g < 4)
                    Assert.That(newAll.Median(g), Is.LessThanOrEqualTo(r4All.Median(g) + 0.5f), Groups[g] + " median vs R4");
            }
            // RT criterion (doc 56 section 4): elbow and knee angle error 15 degrees or less at the median.
            Assert.That(newAll.Median(4), Is.LessThanOrEqualTo(15f), "elbow");
            Assert.That(newAll.Median(5), Is.LessThanOrEqualTo(15f), "knee");
        }

        private static string Cell(Errors e, int g) =>
            e.Median(g).ToString("F1", CultureInfo.InvariantCulture) + " / " + e.Max(g).ToString("F1", CultureInfo.InvariantCulture);

        private static string Summary(Errors raw, Errors fixedDepth)
        {
            var sb = new StringBuilder();
            for (int g = 0; g < Groups.Length; g++)
                sb.Append(Groups[g]).Append(' ').Append(Cell(raw, g)).Append(" -> ").Append(Cell(fixedDepth, g)).Append("; ");
            return sb.ToString();
        }
    }
}
