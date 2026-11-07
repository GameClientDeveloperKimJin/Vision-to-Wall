using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.UI;

namespace VisionToWall.PoseTest.Tests
{
    /// <summary>
    /// Committed rendered-clip data (<c>Tests/Data/&lt;clip&gt;_truth.csv</c>, <c>_detected.csv</c>, made by the
    /// PoseTest menus and <c>pose_from_media.py</c>) against the spec 56 4절 criteria.
    /// <para>
    /// Measured 2026-11-08: every criterion holds except the 3D elbow / knee angle, which fails on every clip
    /// because MediaPipe's world depth puts the shoulders about 20 cm toward the camera and the ankles about 11 cm
    /// away (front-plane x, y angles are within a few degrees). That failure is reported, not hidden: the
    /// <see cref="KnownFailure"/> test pins it so a change in either direction shows up.
    /// </para>
    /// </summary>
    public sealed class PoseClipDataTests
    {
        private static string DataDir => Path.Combine(Application.dataPath, "02_Scripts/PoseTest/Tests/Data");

        private static IEnumerable<string> ClipNames()
        {
            foreach (PoseClip c in PoseTestClips.All) yield return c.Name;
        }

        private static ClipCheck Check(string name)
        {
            PoseClip clip = PoseTestClips.Find(name);
            string truthPath = Path.Combine(DataDir, name + "_truth.csv");
            string detPath = Path.Combine(DataDir, name + "_detected.csv");
            Assert.IsTrue(File.Exists(truthPath), truthPath);
            Assert.IsTrue(File.Exists(detPath), detPath);
            PoseTruth truth = PoseTruth.Parse(File.ReadAllText(truthPath));
            Assert.AreEqual(clip.FrameCount(PoseTestClips.Fps), truth.Count, "frames");
            return PoseCheck.Evaluate(clip, truth.Joints, PoseRecording.Parse(File.ReadAllText(detPath)));
        }

        [TestCaseSource(nameof(ClipNames))]
        public void DetectionAndLeftRight(string clip)
        {
            ClipCheck r = Check(clip);
            Assert.GreaterOrEqual(r.DetectionRate, PoseCheck.MinDetectionRate, "detection rate");
            Assert.AreEqual(0, r.SwapFrames, "left/right swaps");
            Assert.LessOrEqual(r.Shoulder.Median, PoseCheck.MaxAngleMedianDeg, "3D shoulder angle");
        }

        [TestCaseSource(nameof(ClipNames))]
        public void MiniCharacterFollowsDetection(string clip)
        {
            ClipCheck r = Check(clip);
            Assert.LessOrEqual(r.FigElbow.Median, PoseCheck.MaxAngleMedianDeg, "figure elbow");
            Assert.LessOrEqual(r.FigKnee.Median, PoseCheck.MaxAngleMedianDeg, "figure knee");
            Assert.LessOrEqual(r.FigShoulder.Median, PoseCheck.MaxAngleMedianDeg, "figure shoulder");
        }

        [Test]
        public void WalkOut_FigureDisappearsAndReturns()
        {
            ClipCheck r = Check("walk_out");
            Assert.Greater(r.OutFrames, 30, "the clip really leaves the view");
            Assert.AreEqual(0, r.DetectedOut, "no detection with nobody in view");
            Assert.IsTrue(r.Disappeared);
            Assert.IsTrue(r.Reappeared);
            Assert.Less(r.ReappearSec, 0.5f);
        }

        [Test]
        public void FastRaise_FigureReactsWithinTarget()
        {
            // Player promise 3: 0.2-0.3 s (camera exposure, network and display not included here).
            foreach (string clip in new[] { "raise_left_fast", "raise_right_fast" })
            {
                ClipCheck r = Check(clip);
                Assert.Less(r.OnsetDelayFigure, 0.2f, clip);
            }
        }

        [TestCaseSource(nameof(ClipNames))]
        public void KnownFailure(string clip)
        {
            // Pinned: the only failures are the 3D elbow / knee angle medians, and the front-plane angles are fine.
            ClipCheck r = Check(clip);
            foreach (string f in r.Failures)
                Assert.IsTrue(f.StartsWith("팔꿈치 각도") || f.StartsWith("무릎 각도"), "unexpected failure: " + f);
            Assert.Greater(r.Knee.Median, PoseCheck.MaxAngleMedianDeg, "3D knee failure fixed? update spec 56 and this test");
            Assert.LessOrEqual(r.Elbow2D.Median, 10f, "front-plane elbow");
            Assert.LessOrEqual(r.Knee2D.Median, 10f, "front-plane knee");
            Assert.Less(r.DepthShoulder, -0.05f, "shoulders estimated toward the camera");
        }
    }
}
