using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Sensor.Tests
{
    public class FrameDiffDetectorTests
    {
        private const int W = 16;
        private const int H = 16;
        private const float Threshold = 0.15f;
        private const float Eps = 1e-5f;

        private static Color32[] Fill(byte gray)
        {
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(gray, gray, gray, 255);
            return px;
        }

        [Test]
        public void FirstFrame_OnlyPrimes()
        {
            var det = new FrameDiffDetector(W, H);
            var frame = new MotionFrame { Count = 99 };

            Assert.IsFalse(det.Process(Fill(100), Threshold, frame));
            Assert.AreEqual(99, frame.Count, "First frame must not touch the output frame.");
            Assert.IsTrue(det.HasPrevious);
        }

        [Test]
        public void IdenticalInput_ProducesZeroPoints()
        {
            var det = new FrameDiffDetector(W, H);
            var frame = new MotionFrame();

            det.Process(Fill(120), Threshold, frame);
            Assert.IsTrue(det.Process(Fill(120), Threshold, frame));
            Assert.AreEqual(0, frame.Count);
        }

        [Test]
        public void SingleCellChange_ProducesOnePointAtCellCenter()
        {
            const int col = 11;
            const int row = 3;
            var det = new FrameDiffDetector(W, H);
            var frame = new MotionFrame();

            det.Process(Fill(0), Threshold, frame);
            Color32[] next = Fill(0);
            next[row * W + col] = new Color32(255, 255, 255, 255);

            Assert.IsTrue(det.Process(next, Threshold, frame));
            Assert.AreEqual(1, frame.Count);
            Assert.AreEqual((col + 0.5f) / W, frame.Points[0].x, Eps);
            Assert.AreEqual((row + 0.5f) / H, frame.Points[0].y, Eps);
            Assert.AreEqual(1f, frame.Points[0].v, Eps);
            Assert.AreEqual(1f, det.Diff[row * W + col], Eps);
        }

        [Test]
        public void Intensity_ScalesAboveThreshold()
        {
            var det = new FrameDiffDetector(W, H);
            var frame = new MotionFrame();

            det.Process(Fill(0), Threshold, frame);
            Color32[] next = Fill(0);
            next[0] = new Color32(153, 153, 153, 255); // luminance 0.6

            det.Process(next, Threshold, frame);
            Assert.AreEqual(1, frame.Count);
            float expected = (0.6f - Threshold) / (1f - Threshold);
            Assert.AreEqual(expected, frame.Points[0].v, 1e-4f);
        }

        [Test]
        public void DeltaBelowThreshold_Ignored()
        {
            var det = new FrameDiffDetector(W, H);
            var frame = new MotionFrame();

            det.Process(Fill(100), Threshold, frame);
            det.Process(Fill(120), Threshold, frame); // delta ~0.078
            Assert.AreEqual(0, frame.Count);
        }

        [Test]
        public void BuffersSwap_ComparesAgainstPreviousFrame()
        {
            var det = new FrameDiffDetector(W, H);
            var frame = new MotionFrame();

            det.Process(Fill(0), Threshold, frame);
            det.Process(Fill(255), Threshold, frame);
            Assert.AreEqual(W * H, frame.Count);

            det.Process(Fill(255), Threshold, frame);
            Assert.AreEqual(0, frame.Count, "Third frame equals second -> no motion.");

            det.Process(Fill(0), Threshold, frame);
            Assert.AreEqual(W * H, frame.Count);
        }

        [Test]
        public void FlipY_InvertsRow()
        {
            const int col = 2;
            const int inRow = 0;
            var det = new FrameDiffDetector(W, H) { FlipY = true };
            var frame = new MotionFrame();

            det.Process(Fill(0), Threshold, frame);
            Color32[] next = Fill(0);
            next[inRow * W + col] = new Color32(255, 255, 255, 255);
            det.Process(next, Threshold, frame);

            Assert.AreEqual(1, frame.Count);
            Assert.AreEqual((H - 1 + 0.5f) / H, frame.Points[0].y, Eps);
            Assert.AreEqual(1f, det.Diff[(H - 1) * W + col], Eps);
        }

        [Test]
        public void Reset_SkipsNextFrame()
        {
            var det = new FrameDiffDetector(W, H);
            var frame = new MotionFrame();

            det.Process(Fill(0), Threshold, frame);
            det.Reset();
            Assert.IsFalse(det.Process(Fill(255), Threshold, frame));
            Assert.IsTrue(det.Process(Fill(255), Threshold, frame));
            Assert.AreEqual(0, frame.Count);
        }

        [Test]
        public void NativeArrayOverload_MatchesManaged()
        {
            var det = new FrameDiffDetector(W, H);
            var frame = new MotionFrame();
            var a = new NativeArray<Color32>(Fill(0), Allocator.Temp);
            Color32[] nextManaged = Fill(0);
            nextManaged[5] = new Color32(255, 255, 255, 255);
            var b = new NativeArray<Color32>(nextManaged, Allocator.Temp);
            try
            {
                det.Process(a, Threshold, frame);
                det.Process(b, Threshold, frame);
                Assert.AreEqual(1, frame.Count);
                Assert.AreEqual((5 + 0.5f) / W, frame.Points[0].x, Eps);
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        [Test]
        public void Constructor_RejectsGridAboveMaxPoints()
        {
            Assert.Throws<System.ArgumentException>(() => new FrameDiffDetector(MotionProtocol.MaxPoints, 2));
        }
    }
}
