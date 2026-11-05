using NUnit.Framework;
using UnityEngine;

namespace VisionToWall.Render.Tests
{
    public class RippleBufferTests
    {
        [Test]
        public void NearbyRippleWithinSuppressWindow_IsRejected()
        {
            var buffer = new RippleBuffer();
            Assert.IsTrue(buffer.TryAdd(0.5f, 0.5f, 10f, 1f));
            Assert.IsFalse(buffer.TryAdd(0.52f, 0.51f, 10.1f, 1f));
            Assert.AreEqual(1, buffer.Count);
        }

        [Test]
        public void NearbyRippleAfterSuppressWindow_IsAccepted()
        {
            var buffer = new RippleBuffer();
            buffer.TryAdd(0.5f, 0.5f, 10f, 1f);
            Assert.IsTrue(buffer.TryAdd(0.5f, 0.5f, 10f + RippleBuffer.DefaultSuppressSec + 0.01f, 1f));
            Assert.AreEqual(2, buffer.Count);
        }

        [Test]
        public void DistantRippleWithinSuppressWindow_IsAccepted()
        {
            var buffer = new RippleBuffer();
            buffer.TryAdd(0.5f, 0.5f, 10f, 1f);
            Assert.IsTrue(buffer.TryAdd(0.5f + RippleBuffer.DefaultMinDistance + 0.01f, 0.5f, 10.05f, 1f));
        }

        [Test]
        public void MoreThanCapacity_OverwritesOldest()
        {
            var buffer = new RippleBuffer();
            int total = RippleBuffer.Capacity + 3;
            for (int i = 0; i < total; i++)
            {
                // 1 s apart so suppression never applies.
                Assert.IsTrue(buffer.TryAdd(0.5f, 0.5f, i, 1f));
            }

            Assert.AreEqual(RippleBuffer.Capacity, buffer.Count);

            // Slots 0..2 hold the newest three (32, 33, 34); slot 3 holds the oldest survivor (3).
            Assert.AreEqual(32f, buffer.Data[0].z);
            Assert.AreEqual(34f, buffer.Data[2].z);
            Assert.AreEqual(3f, buffer.Data[3].z);

            float min = float.MaxValue;
            foreach (Vector4 r in buffer.Data) min = Mathf.Min(min, r.z);
            Assert.AreEqual(3f, min, "Entries 0..2 must have been overwritten.");
        }

        [Test]
        public void Clear_ResetsCount()
        {
            var buffer = new RippleBuffer();
            buffer.TryAdd(0.1f, 0.1f, 0f, 1f);
            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
            Assert.IsTrue(buffer.TryAdd(0.1f, 0.1f, 0.01f, 1f));
        }
    }
}
