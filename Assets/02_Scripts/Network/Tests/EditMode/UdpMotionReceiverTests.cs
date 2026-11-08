using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;
using Object = UnityEngine.Object;

namespace VisionToWall.Network.Tests
{
    /// <summary>
    /// Loopback through a real socket. Covers the idle wait (Socket.Poll instead of a throwing ReceiveTimeout):
    /// packets after an idle period still arrive in order, and stopping an idle receiver does not hang.
    /// </summary>
    public sealed class UdpMotionReceiverTests
    {
        private const int Port = 39100;

        private GameObject _go;
        private UdpMotionReceiver _rx;
        private UdpClient _tx;
        private MethodInfo _update;
        private MethodInfo _stop;
        private readonly byte[] _buffer = new byte[MotionProtocol.MaxPacketSize];

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("UdpMotionReceiverTest");
            _rx = _go.AddComponent<UdpMotionReceiver>();
            _rx.Initialize(new AppConfig(), Port);
            _tx = new UdpClient();
            _tx.Connect("127.0.0.1", Port);
            _update = typeof(UdpMotionReceiver).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            _stop = typeof(UdpMotionReceiver).GetMethod("StopWorker", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            _tx?.Close();
            if (_go != null)
            {
                _stop.Invoke(_rx, null); // EditMode does not call OnDisable on plain MonoBehaviours
                Object.DestroyImmediate(_go);
            }
        }

        private void Send(uint seq, int count)
        {
            var f = new MotionFrame { Count = count };
            for (int i = 0; i < count; i++) f.Points[i] = new MotionPoint(0.1f * i, 0.5f, 1f);
            int len = MotionPacketSerializer.Write(f, seq, _buffer);
            _tx.Send(_buffer, len);
        }

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
        public void ReceivesAfterIdleWaits_InOrder()
        {
            Assert.IsTrue(_rx.IsRunning);
            System.Threading.Thread.Sleep(1200); // at least two idle waits time out

            int events = 0;
            uint lastSeq = 0;
            _rx.FrameReceived += f => { events++; lastSeq = f.Seq; };
            for (uint s = 1; s <= 5; s++) Send(s, (int)s);
            Pump(5);

            Assert.AreEqual(5, _rx.PacketsReceived);
            Assert.AreEqual(0, _rx.PacketsDropped);
            Assert.AreEqual(5, events);
            Assert.AreEqual(5u, lastSeq);
        }

        [Test]
        public void IdleReceiver_StopsWithoutWaitingForTheTimeout()
        {
            System.Threading.Thread.Sleep(700);
            var sw = Stopwatch.StartNew();
            _stop.Invoke(_rx, null);
            sw.Stop();

            Assert.IsFalse(_rx.IsRunning);
            // The Join limit is 1000 ms; an idle wait is 500 ms at most even if Close does not wake it.
            Assert.Less(sw.ElapsedMilliseconds, 700, "stop took " + sw.ElapsedMilliseconds + " ms");
            Object.DestroyImmediate(_go);
            _go = null;
        }
    }
}
