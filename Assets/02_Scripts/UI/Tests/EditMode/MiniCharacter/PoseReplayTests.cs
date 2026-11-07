using System.IO;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI.Tests
{
    /// <summary>
    /// Replays a real MediaPipe PoseLandmarker recording (Tools/HandSensor, R2 human check 2026-11-08: right arm
    /// raised about 5-10 s, left arm about 11-13.5 s, seated, legs out of frame) through <see cref="PoseSmoother"/>.
    /// Checks the figure without anyone posing in front of the camera.
    /// </summary>
    public sealed class PoseReplayTests
    {
        private const string ArmsLeftRight = "02_Scripts/UI/Tests/EditMode/MiniCharacter/Data/pose_arms_lr.csv";
        private const float Dt = 1f / 60f;

        private static PoseRecording Load(string relative) =>
            PoseRecording.Parse(File.ReadAllText(Path.Combine(Application.dataPath, relative)));

        /// <summary>Plays the recording at 60 fps and calls <paramref name="probe"/> after every tick.</summary>
        private static void Replay(PoseRecording rec, PoseSmoother s, System.Action<float> probe)
        {
            var frame = new PoseFrame();
            int next = 0;
            float end = rec.DurationSec + 0.5f;
            for (float t = 0f; t < end; t += Dt)
            {
                while (next < rec.Count && rec[next].TimestampMs / 1000f <= t)
                {
                    rec.CopyTo(next, frame, (uint)(next + 1));
                    s.Push(frame, t);
                    next++;
                }

                s.Tick(t, Dt);
                probe(t);
            }
        }

        private struct Window
        {
            public int Ticks;
            public int Raised;      // raised wrist clearly above its shoulder
            public int CorrectSide; // on the mirror side (player's right = screen right = +x)
            public int Higher;      // raised wrist above the other wrist
            public int Bright;      // raised wrist drawn at full alpha
        }

        [Test]
        public void Parse_MatchesRecording()
        {
            PoseRecording rec = Load(ArmsLeftRight);
            Assert.AreEqual(568, rec.Count);
            Assert.AreEqual(19.203f, rec.DurationSec, 1e-3f);

            int empty = 0;
            for (int i = 0; i < rec.Count; i++) if (!rec[i].HasPerson) empty++;
            Assert.AreEqual(1, empty);

            var f = new PoseFrame();
            rec.CopyTo(0, f, 1);
            Assert.IsTrue(f.HasPerson);
            Assert.AreEqual(31u, f.TimestampMs);
            Assert.AreEqual(0.4885f, f.Screen[(int)PoseJoint.Nose].x, 1e-4f);
            Assert.AreEqual(0.9987f, f.Visibility[(int)PoseJoint.Nose], 1e-4f);
        }

        [Test]
        public void Parse_RejectsWrongHeader()
        {
            Assert.Throws<System.FormatException>(() => PoseRecording.Parse("a,b\n1,2"));
        }

        [Test]
        public void RealRecording_ArmsFollowOnMirrorSide()
        {
            PoseRecording rec = Load(ArmsLeftRight);
            var s = new PoseSmoother();
            var right = new Window();
            var left = new Window();
            int settledTicks = 0, personTicks = 0;

            Replay(rec, s, t =>
            {
                if (t > 0.5f && t < 19f)
                {
                    personTicks++;
                    if (s.Settled && s.Presence > 0.99f) settledTicks++;
                }

                Vector2 rw = s.Joint(PoseJoint.RightWrist), lw = s.Joint(PoseJoint.LeftWrist);
                Vector2 rs = s.Joint(PoseJoint.RightShoulder), ls = s.Joint(PoseJoint.LeftShoulder);

                if (t >= 6.5f && t <= 9.5f) Count(ref right, rw, rs, lw, rw.x > 0f, s.JointAlpha(PoseJoint.RightWrist));
                if (t >= 11.6f && t <= 12.5f) Count(ref left, lw, ls, rw, lw.x < 0f, s.JointAlpha(PoseJoint.LeftWrist));
            });

            // Seated with legs out of frame: the figure must still stand (world joints).
            Assert.Greater(settledTicks, personTicks * 0.9f, "figure shown for most of the recording");

            AssertWindow("right arm", right);
            AssertWindow("left arm", left);
        }

        private static void Count(ref Window w, Vector2 wrist, Vector2 shoulder, Vector2 otherWrist, bool side, float alpha)
        {
            w.Ticks++;
            if (wrist.y > shoulder.y + 0.5f) w.Raised++;
            if (side) w.CorrectSide++;
            if (wrist.y > otherWrist.y) w.Higher++;
            if (alpha > 0.99f) w.Bright++;
        }

        private static void AssertWindow(string name, Window w)
        {
            Assert.Greater(w.Ticks, 0, name);
            float min = w.Ticks * 0.9f;
            Assert.Greater(w.Raised, min, $"{name}: raised {w.Raised}/{w.Ticks}");
            Assert.Greater(w.CorrectSide, min, $"{name}: side {w.CorrectSide}/{w.Ticks}");
            Assert.Greater(w.Higher, min, $"{name}: above other wrist {w.Higher}/{w.Ticks}");
            Assert.Greater(w.Bright, min, $"{name}: bright {w.Bright}/{w.Ticks}");
        }
    }
}
