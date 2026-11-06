using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game.Tests
{
    public sealed class PlayerInputFilterTests
    {
        private const float Tolerance = 1e-4f;
        private const float Dt = 1f / 60f;

        private static MotionFrame Frame(params MotionPoint[] points)
        {
            var frame = new MotionFrame { Count = points.Length };
            points.CopyTo(frame.Points, 0);
            return frame;
        }

        private static MotionFrame Point(float x, float y, float v = 1f) => Frame(new MotionPoint(x, y, v));

        /// <summary>Ticks from <paramref name="from"/> to <paramref name="to"/> at 60 Hz, returns the last sample.</summary>
        private static PlayerSample Run(PlayerInputFilter f, float from, float to)
        {
            PlayerSample s = f.Current;
            for (float t = from + Dt; t <= to; t += Dt) s = f.Tick(t, Dt);
            return s;
        }

        // ---------- source priority ----------

        [Test]
        public void NoInput_HasNoPosition()
        {
            PlayerSample s = new PlayerInputFilter(true).Tick(1f, Dt);
            Assert.IsFalse(s.hasPosition);
            Assert.AreEqual(PlayerPositionSource.None, s.source);
            Assert.AreEqual(0f, s.motion);
        }

        [Test]
        public void BodyWinsOverMotion()
        {
            var f = new PlayerInputFilter(true);
            f.AddBodyFrame(Point(0.2f, 0.3f), 1f);
            f.AddMotionFrame(Point(0.8f, 0.9f), 1f);
            PlayerSample s = f.Tick(1f, Dt);
            Assert.AreEqual(PlayerPositionSource.Body, s.source);
            Assert.AreEqual(0.2f, s.position.x, Tolerance);
            Assert.AreEqual(0.3f, s.position.y, Tolerance);
        }

        [Test]
        public void MotionUsedWithoutBody()
        {
            var f = new PlayerInputFilter(true);
            f.AddMotionFrame(Point(0.8f, 0.9f), 1f);
            PlayerSample s = f.Tick(1f, Dt);
            Assert.AreEqual(PlayerPositionSource.MotionCentroid, s.source);
            Assert.AreEqual(0.8f, s.position.x, Tolerance);
        }

        [Test]
        public void EmptyBodyPacket_DoesNotRefreshBody()
        {
            var f = new PlayerInputFilter(true);
            f.AddBodyFrame(Point(0.2f, 0.3f), 1f);
            f.AddBodyFrame(Frame(), 1.29f);
            Assert.AreEqual(PlayerPositionSource.Body, f.Tick(1.29f, Dt).source);
            f.AddMotionFrame(Point(0.8f, 0.9f), 1.31f);
            f.AddBodyFrame(Frame(), 1.31f);
            Assert.AreEqual(PlayerPositionSource.MotionCentroid, f.Tick(1.31f, Dt).source);
        }

        // ---------- source change ----------

        [Test]
        public void BodyGoesStale_FallsBackAfterFreshWindow()
        {
            var f = new PlayerInputFilter(true);
            f.AddBodyFrame(Point(0.2f, 0.3f), 1f);
            f.AddMotionFrame(Point(0.8f, 0.9f), 1.29f);
            Assert.AreEqual(PlayerPositionSource.Body, f.Tick(1.29f, Dt).source);
            f.AddMotionFrame(Point(0.8f, 0.9f), 1.31f);
            Assert.AreEqual(PlayerPositionSource.MotionCentroid, f.Tick(1.31f, Dt).source);
        }

        [Test]
        public void SourceChange_DoesNotJump()
        {
            var f = new PlayerInputFilter(true);
            f.AddBodyFrame(Point(0.2f, 0.5f), 1f);
            f.Tick(1f, Dt);
            f.AddMotionFrame(Point(0.8f, 0.5f), 1.4f);
            PlayerSample s = f.Tick(1.4f, Dt);
            Assert.AreEqual(PlayerPositionSource.MotionCentroid, s.source);
            float expected = 0.2f + 0.6f * (1f - Mathf.Exp(-Dt / PlayerInputFilter.PositionTauSec));
            Assert.AreEqual(expected, s.position.x, Tolerance);
        }

        [Test]
        public void PositionSmoothing_ReachesTargetByTimeConstant()
        {
            var f = new PlayerInputFilter(true);
            f.AddBodyFrame(Point(0f, 0f), 1f);
            f.Tick(1f, Dt);
            f.AddBodyFrame(Point(1f, 0f), 1f);
            PlayerSample s = f.Tick(1f, PlayerInputFilter.PositionTauSec);
            Assert.AreEqual(1f - Mathf.Exp(-1f), s.position.x, Tolerance);
        }

        // ---------- hold ----------

        [Test]
        public void AllSourcesGone_HoldsOneSecondThenClears()
        {
            var f = new PlayerInputFilter(true);
            f.AddBodyFrame(Point(0.2f, 0.3f), 1f);
            f.Tick(1f, Dt);
            float lastSeen = 1f + PlayerInputFilter.SourceFreshSec;
            f.Tick(lastSeen, Dt);

            PlayerSample held = f.Tick(lastSeen + 0.9f, Dt);
            Assert.IsTrue(held.hasPosition);
            Assert.AreEqual(PlayerPositionSource.Body, held.source);
            Assert.AreEqual(0.2f, held.position.x, 0.01f);

            PlayerSample cleared = f.Tick(lastSeen + 1.1f, Dt);
            Assert.IsFalse(cleared.hasPosition);
            Assert.AreEqual(PlayerPositionSource.None, cleared.source);
        }

        [Test]
        public void Reacquire_AfterClear_SnapsToNewPosition()
        {
            var f = new PlayerInputFilter(true);
            f.AddBodyFrame(Point(0.2f, 0.3f), 1f);
            f.Tick(1f, Dt);
            f.Tick(3f, Dt);
            f.Tick(5f, Dt);
            f.AddBodyFrame(Point(0.9f, 0.1f), 6f);
            PlayerSample s = f.Tick(6f, Dt);
            Assert.AreEqual(0.9f, s.position.x, Tolerance);
            Assert.AreEqual(0.1f, s.position.y, Tolerance);
        }

        // ---------- centroid ----------

        [Test]
        public void Centroid_IsWeightedByIntensity()
        {
            var f = new PlayerInputFilter(true);
            f.AddMotionFrame(Frame(new MotionPoint(0f, 0f, 0.25f), new MotionPoint(1f, 1f, 0.75f)), 1f);
            PlayerSample s = f.Tick(1f, Dt);
            Assert.AreEqual(0.75f, s.position.x, Tolerance);
            Assert.AreEqual(0.75f, s.position.y, Tolerance);
        }

        [Test]
        public void Centroid_ZeroIntensity_IsIgnored()
        {
            var f = new PlayerInputFilter(true);
            f.AddMotionFrame(Point(0.5f, 0.5f, 0f), 1f);
            Assert.IsFalse(f.Tick(1f, Dt).hasPosition);
        }

        [Test]
        public void Centroid_EmptyFrames_KeepItFreshWindowThenExpire()
        {
            var f = new PlayerInputFilter(true);
            f.AddMotionFrame(Point(0.5f, 0.5f), 1f);
            f.Tick(1f, Dt);
            f.AddMotionFrame(Frame(), 1.1f);
            Assert.AreEqual(PlayerPositionSource.MotionCentroid, f.Tick(1.1f, Dt).source);
            f.AddMotionFrame(Frame(), 2.5f);
            Assert.IsFalse(f.Tick(2.5f, Dt).hasPosition);
        }

        // ---------- motion intensity ----------

        [Test]
        public void ExternalMode_UsesMaxV()
        {
            var f = new PlayerInputFilter(true);
            f.AddMotionFrame(Frame(new MotionPoint(0f, 0f, 0.3f), new MotionPoint(1f, 1f, 0.6f)), 1f);
            Assert.AreEqual(0.6f, f.Tick(1f, Dt).motion, Tolerance);
        }

        [Test]
        public void WebcamMode_UsesSumOverSaturation()
        {
            var f = new PlayerInputFilter(false);
            f.AddMotionFrame(Frame(new MotionPoint(0f, 0f, 1f), new MotionPoint(1f, 1f, 1f)), 1f);
            Assert.AreEqual(2f / PlayerInputFilter.WebcamActivitySaturation, f.Tick(1f, Dt).motion, Tolerance);
        }

        [Test]
        public void WebcamMode_ClampsToOne()
        {
            var f = new PlayerInputFilter(false);
            var points = new MotionPoint[20];
            for (int i = 0; i < points.Length; i++) points[i] = new MotionPoint(0.5f, 0.5f, 1f);
            f.AddMotionFrame(Frame(points), 1f);
            Assert.AreEqual(1f, f.Tick(1f, Dt).motion, Tolerance);
        }

        [Test]
        public void Motion_RisesAtOnce_FallsByTimeConstant()
        {
            var f = new PlayerInputFilter(true);
            f.AddMotionFrame(Point(0.5f, 0.5f, 1f), 1f);
            Assert.AreEqual(1f, f.Tick(1f, Dt).motion, Tolerance);
            f.AddMotionFrame(Frame(), 1f);
            float fallen = f.Tick(1f, PlayerInputFilter.MotionDecayTauSec).motion;
            Assert.AreEqual(Mathf.Exp(-1f), fallen, Tolerance);
        }

        [Test]
        public void Motion_PeakBetweenTicks_IsNotLost()
        {
            var f = new PlayerInputFilter(true);
            f.AddMotionFrame(Point(0.5f, 0.5f, 0.9f), 1f);
            f.AddMotionFrame(Point(0.5f, 0.5f, 0.1f), 1f);
            float expected = 0.1f + 0.8f * Mathf.Exp(-Dt / PlayerInputFilter.MotionDecayTauSec);
            Assert.AreEqual(expected, f.Tick(1f, Dt).motion, Tolerance);
        }

        [Test]
        public void Motion_DecaysWhenStreamStops()
        {
            var f = new PlayerInputFilter(true);
            f.AddMotionFrame(Point(0.5f, 0.5f, 1f), 1f);
            f.Tick(1f, Dt);
            Assert.Less(Run(f, 1f, 3f).motion, 0.05f);
        }

        [Test]
        public void Motion_IgnoresBodyStream()
        {
            var f = new PlayerInputFilter(true);
            f.AddBodyFrame(Point(0.2f, 0.3f, 1f), 1f);
            Assert.AreEqual(0f, f.Tick(1f, Dt).motion);
        }
    }
}
