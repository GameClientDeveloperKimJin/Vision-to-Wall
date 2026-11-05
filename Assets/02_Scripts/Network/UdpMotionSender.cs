using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Network
{
    /// <summary>
    /// FR-05 sender. <see cref="Submit"/> copies into a latest-wins slot on the main thread;
    /// a background thread serializes and sends. If the thread falls behind, the pending frame
    /// is overwritten and <see cref="FramesSkipped"/> increments.
    /// </summary>
    public sealed class UdpMotionSender : MonoBehaviour, IMotionFrameSink
    {
        /// <summary>Wait timeout so the send thread re-checks the stop flag.</summary>
        private const int WaitTimeoutMs = 500;
        private const int JoinTimeoutMs = 1000;

        private readonly object _slotLock = new object();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);

        // Pending is written by Submit; Work is owned by the send thread. Swapped under _slotLock.
        private MotionFrame _pending = new MotionFrame();
        private MotionFrame _work = new MotionFrame();
        private bool _hasPending;

        private readonly byte[] _sendBuffer = new byte[MotionProtocol.MaxPacketSize];

        private AppConfig _config;
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;

        private uint _seq;
        private int _framesSent;
        private int _framesSkipped;
        private int _sendErrors;

        public int FramesSent => Volatile.Read(ref _framesSent);
        public int FramesSkipped => Volatile.Read(ref _framesSkipped);
        /// <summary>Socket errors while sending (e.g. ICMP port unreachable when no receiver is bound).</summary>
        public int SendErrors => Volatile.Read(ref _sendErrors);
        public bool IsRunning => _running;

        /// <summary>Uses <c>udpIp</c> and <c>udpPort</c>. Restart required to change them.</summary>
        public void Initialize(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            StopWorker();
            if (isActiveAndEnabled) StartWorker();
        }

        public void Submit(MotionFrame frame)
        {
            if (!_running || frame == null) return;

            lock (_slotLock)
            {
                if (_hasPending) Interlocked.Increment(ref _framesSkipped);
                _pending.CopyFrom(frame);
                _hasPending = true;
            }
            _signal.Set();
        }

        private void OnEnable()
        {
            if (_config != null) StartWorker();
        }

        private void OnDisable() => StopWorker();

        private void OnDestroy() => _signal.Dispose();

        private void StartWorker()
        {
            if (_running) return;

            try
            {
                _client = new UdpClient();
                _client.Connect(_config.udpIp, _config.udpPort);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UdpMotionSender] Connect {_config.udpIp}:{_config.udpPort} failed: {e.Message}");
                _client?.Close();
                _client = null;
                return;
            }

            lock (_slotLock) _hasPending = false;
            _seq = 0;
            _running = true;
            _thread = new Thread(SendLoop) { Name = "UdpMotionSender", IsBackground = true };
            _thread.Start();
        }

        private void StopWorker()
        {
            if (!_running && _thread == null) return;

            _running = false;
            _signal.Set();
            _client?.Close();
            if (_thread != null && !_thread.Join(JoinTimeoutMs))
                Debug.LogWarning("[UdpMotionSender] Send thread did not exit in time.");
            _thread = null;
            _client = null;
        }

        private void SendLoop()
        {
            UdpClient client = _client;
            while (_running)
            {
                if (!_signal.WaitOne(WaitTimeoutMs)) continue;
                if (!_running) break;

                lock (_slotLock)
                {
                    if (!_hasPending) continue;
                    MotionFrame tmp = _work;
                    _work = _pending;
                    _pending = tmp;
                    _hasPending = false;
                }

                _work.TimestampMs = (uint)Environment.TickCount;
                int length = MotionPacketSerializer.Write(_work, ++_seq, _sendBuffer);

                try
                {
                    client.Send(_sendBuffer, length);
                    Interlocked.Increment(ref _framesSent);
                }
                catch (SocketException)
                {
                    Interlocked.Increment(ref _sendErrors);
                }
                catch (ObjectDisposedException)
                {
                    if (!_running) break;
                    Interlocked.Increment(ref _sendErrors);
                }
            }
        }
    }
}
