using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.Network;

namespace VisionToWall.Game.Tests
{
    /// <summary>
    /// Update and OnDisable do not run on their own in EditMode, so these tests call them directly.
    /// </summary>
    public sealed class UdpMotionReceiverPortTests
    {
        private const int WaitMs = 2000;

        private GameObject _go;
        private UdpMotionReceiver _receiver;
        private int _received;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("UdpMotionReceiverPortTests");
            _receiver = _go.AddComponent<UdpMotionReceiver>();
            _received = 0;
            _receiver.FrameReceived += _ => _received++;
        }

        [TearDown]
        public void TearDown()
        {
            Call("OnDisable");
            UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void PortOverload_ReceivesOnGivenPort_NotConfigPort()
        {
            int port = FreePort();
            int otherPort = FreePort(port);
            _receiver.Initialize(new AppConfig { udpPort = otherPort }, port);

            Send(otherPort, 1);
            Send(port, 2);

            Assert.IsTrue(WaitForFrame(), "packet on the given port was not received");
            Assert.AreEqual(1, _received);
            Assert.AreEqual(2u, _receiver.LastSeq);
        }

        [Test]
        public void ConfigOverload_StillUsesUdpPort()
        {
            int port = FreePort();
            _receiver.Initialize(new AppConfig { udpPort = port });

            Send(port, 1);

            Assert.IsTrue(WaitForFrame(), "packet on udpPort was not received");
        }

        [Test]
        public void PortOverload_RejectsBadArguments()
        {
            Assert.Throws<ArgumentNullException>(() => _receiver.Initialize(null, 9001));
            Assert.Throws<ArgumentNullException>(() => _receiver.Initialize(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => _receiver.Initialize(new AppConfig(), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _receiver.Initialize(new AppConfig(), 65536));
        }

        private void Call(string method)
        {
            typeof(UdpMotionReceiver)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_receiver, null);
        }

        private bool WaitForFrame()
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < WaitMs)
            {
                Call("Update");
                if (_received > 0) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        private static void Send(int port, uint seq)
        {
            var frame = new MotionFrame { Count = 1 };
            frame.Points[0] = new MotionPoint(0.5f, 0.5f, 1f);
            var buffer = new byte[MotionProtocol.MaxPacketSize];
            int length = MotionPacketSerializer.Write(frame, seq, buffer);
            using (var client = new UdpClient())
                client.Send(buffer, length, new IPEndPoint(IPAddress.Loopback, port));
        }

        private static int FreePort(int except = 0)
        {
            while (true)
            {
                using (var probe = new UdpClient(0))
                {
                    int port = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
                    if (port != except) return port;
                }
            }
        }
    }
}
