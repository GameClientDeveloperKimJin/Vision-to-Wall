using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;
using Object = UnityEngine.Object;

namespace VisionToWall.Network.Tests
{
    /// <summary>Loopback through a real socket on a test port. Update is called by reflection (EditMode has no loop).</summary>
    public sealed class UdpPoseReceiverTests
    {
        private const int Port = 39102;

        private GameObject _go;
        private UdpPoseReceiver _rx;
        private UdpClient _tx;
        private MethodInfo _update;
        private readonly byte[] _buffer = new byte[PoseProtocol.PacketSize + 16];

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("UdpPoseReceiverTest");
            _rx = _go.AddComponent<UdpPoseReceiver>();
            _rx.Initialize(new AppConfig(), Port);
            _tx = new UdpClient();
            _tx.Connect("127.0.0.1", Port);
            _update = typeof(UdpPoseReceiver).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            _tx?.Close();
            if (_go != null)
            {
                // EditMode does not call OnDisable on plain MonoBehaviours, so stop the thread directly.
                typeof(UdpPoseReceiver).GetMethod("StopWorker", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(_rx, null);
                Object.DestroyImmediate(_go);
            }
        }

        private void Send(uint seq, bool person, float wristX = 0.5f)
        {
            var f = new PoseFrame { HasPerson = person };
            for (int i = 0; i < PoseProtocol.JointCount; i++) f.Visibility[i] = 1f;
            f.Screen[(int)PoseJoint.RightWrist] = new Vector2(wristX, 0.5f);
            int len = PosePacketSerializer.Write(f, seq, _buffer);
            _tx.Send(_buffer, len);
        }

        /// <summary>Dispatches until <paramref name="count"/> packets were received and dropped in total, or 2 s.</summary>
        private void Pump(int count)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2000)
            {
                _update.Invoke(_rx, null);
                if (_rx.PacketsReceived + _rx.PacketsDropped >= count) return;
                System.Threading.Thread.Sleep(5);
            }
        }

        [Test]
        public void ReceivesFrameAndCountZero()
        {
            Assert.IsTrue(_rx.IsRunning);
            Assert.Less(_rx.LastPoseTime, 0f);
            Assert.IsFalse(_rx.Latest.HasPerson);

            int events = 0;
            _rx.PoseReceived += _ => events++;

            Send(1, true, 0.25f);
            Pump(1);
            Assert.AreEqual(1, _rx.PacketsReceived);
            Assert.AreEqual(1, events);
            Assert.IsTrue(_rx.Latest.HasPerson);
            Assert.AreEqual(0.25f, _rx.Latest.Screen[(int)PoseJoint.RightWrist].x);
            Assert.GreaterOrEqual(_rx.LastPoseTime, 0f);

            Send(2, false);
            Pump(2);
            Assert.AreEqual(2, _rx.PacketsReceived);
            Assert.AreEqual(1, _rx.EmptyPacketsReceived);
            Assert.IsFalse(_rx.Latest.HasPerson);
            Assert.AreEqual(2u, _rx.LastSeq);
        }

        [Test]
        public void IdleWait_ThenReceives_AndStopsQuickly()
        {
            System.Threading.Thread.Sleep(1200); // idle waits (Socket.Poll) time out without exceptions
            Send(1, true);
            Pump(1);
            Assert.AreEqual(1, _rx.PacketsReceived);
            Assert.AreEqual(0, _rx.PacketsDropped);

            System.Threading.Thread.Sleep(700);
            var sw = Stopwatch.StartNew();
            typeof(UdpPoseReceiver).GetMethod("StopWorker", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_rx, null);
            sw.Stop();
            Assert.IsFalse(_rx.IsRunning);
            Assert.Less(sw.ElapsedMilliseconds, 700, "stop took " + sw.ElapsedMilliseconds + " ms");
        }

        [Test]
        public void DropsInvalidAndOldSeq()
        {
            Send(5, true);
            Pump(1);

            Send(4, true); // regression
            Send(5, true); // duplicate
            _tx.Send(new byte[] { 1, 2, 3 }, 3); // too short
            var bad = new byte[PoseProtocol.PacketSize];
            System.Array.Copy(_buffer, bad, bad.Length);
            bad[0] ^= 0xFF; // magic
            _tx.Send(bad, bad.Length);
            Send(6, true);
            Pump(6);

            Assert.AreEqual(2, _rx.PacketsReceived);
            Assert.AreEqual(2, _rx.DroppedSeq);
            Assert.AreEqual(2, _rx.DroppedInvalid);
            Assert.AreEqual(4, _rx.PacketsDropped);
            Assert.AreEqual(6u, _rx.LastSeq);
        }

        [Test]
        public void PoolExhausted_KeepsNewest()
        {
            // 20 packets without dispatching: the pool (8) overflows, the oldest are dropped.
            for (uint s = 1; s <= 20; s++) Send(s, true, s / 100f);
            var sw = Stopwatch.StartNew();
            while (_rx.DroppedPoolExhausted < 12 && sw.ElapsedMilliseconds < 2000) System.Threading.Thread.Sleep(5);
            Pump(20);

            Assert.AreEqual(12, _rx.DroppedPoolExhausted);
            Assert.AreEqual(8, _rx.PacketsReceived);
            Assert.AreEqual(20u, _rx.LastSeq);
            Assert.AreEqual(0.2f, _rx.Latest.Screen[(int)PoseJoint.RightWrist].x, 1e-6f);
        }

        [Test]
        public void PortZero_StaysClosed()
        {
            var go = new GameObject("UdpPoseReceiverOff");
            try
            {
                var rx = go.AddComponent<UdpPoseReceiver>();
                rx.Initialize(new AppConfig { udpPosePort = 0 });
                Assert.IsFalse(rx.IsRunning);
                Assert.AreEqual(0, rx.Port);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
