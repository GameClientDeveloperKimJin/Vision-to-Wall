using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Network
{
    /// <summary>
    /// FR-05 receiver. A background thread reads datagrams with <c>Socket.Receive(byte[])</c> (no per-packet
    /// allocation), parses them into pooled frames and queues them. <see cref="Update"/> raises
    /// <see cref="FrameReceived"/> on the main thread and returns frames to the pool.
    /// </summary>
    public sealed class UdpMotionReceiver : MonoBehaviour, IPointSource, INetworkStats
    {
        private const int PoolSize = 8;
        /// <summary>Max frames dispatched per Update.</summary>
        private const int MaxDispatchPerFrame = PoolSize;
        /// <summary>Receive timeout so the thread re-checks the stop flag.</summary>
        private const int ReceiveTimeoutMs = 500;
        /// <summary>
        /// Wait for a datagram this long before re-checking the stop flag. Poll returns false on an idle socket instead
        /// of the SocketException a ReceiveTimeout throws (about 4 KB of garbage each, twice a second per idle port).
        /// </summary>
        private const int PollMicroseconds = ReceiveTimeoutMs * 1000;
        private const int JoinTimeoutMs = 1000;
        /// <summary>OS receive buffer: ~20 max-size packets of headroom for main-thread hitches.</summary>
        private const int SocketReceiveBufferBytes = 1 << 20;

        public event Action<MotionFrame> FrameReceived;

        private readonly ConcurrentQueue<MotionFrame> _free = new ConcurrentQueue<MotionFrame>();
        private readonly ConcurrentQueue<MotionFrame> _ready = new ConcurrentQueue<MotionFrame>();
        private readonly byte[] _receiveBuffer = new byte[MotionProtocol.MaxPacketSize];

        private AppConfig _config;
        private int _port;
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;

        private int _packetsReceived;
        private int _dropPool;
        private int _dropInvalid;
        private int _dropSeq;
        private int _dropSocket;
        private uint _lastSeq;
        private float _lastPacketTime = -1f;

        public int PacketsReceived => _packetsReceived;
        public int PacketsDropped => DroppedPoolExhausted + DroppedInvalid + DroppedSeq + DroppedSocket;

        /// <summary>No free frame: main thread is not dispatching fast enough (hitch, unfocused editor).</summary>
        public int DroppedPoolExhausted => Volatile.Read(ref _dropPool);
        /// <summary>Bad magic, length or count.</summary>
        public int DroppedInvalid => Volatile.Read(ref _dropInvalid);
        /// <summary>Seq regression or duplicate (e.g. two senders on one port).</summary>
        public int DroppedSeq => Volatile.Read(ref _dropSeq);
        /// <summary>Oversized datagram or other socket error.</summary>
        public int DroppedSocket => Volatile.Read(ref _dropSocket);
        public uint LastSeq => _lastSeq;
        /// <summary>Time.unscaledTime of the last dispatched packet; -1 before the first one.</summary>
        public float LastPacketTime => _lastPacketTime;
        public bool IsRunning => _running;

        /// <summary>Uses <c>udpPort</c>. Restart required to change it.</summary>
        public void Initialize(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Initialize(config, config.udpPort);
        }

        /// <summary>Binds <paramref name="port"/> instead of <c>udpPort</c> (e.g. <c>udpBodyPort</c>).</summary>
        public void Initialize(AppConfig config, int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _port = port;
            StopWorker();
            if (isActiveAndEnabled) StartWorker();
        }

        private bool _poolReady;

        // Lazy so Initialize works even if called before this component's Awake.
        private void EnsurePool()
        {
            if (_poolReady) return;
            _poolReady = true;
            for (int i = 0; i < PoolSize; i++) _free.Enqueue(new MotionFrame());
        }

        private void Awake() => EnsurePool();

        private void OnEnable()
        {
            if (_config != null) StartWorker();
        }

        private void OnDisable() => StopWorker();

        private void Update()
        {
            for (int i = 0; i < MaxDispatchPerFrame; i++)
            {
                if (!_ready.TryDequeue(out MotionFrame frame)) break;

                _packetsReceived++;
                _lastSeq = frame.Seq;
                _lastPacketTime = Time.unscaledTime;
                try
                {
                    FrameReceived?.Invoke(frame);
                }
                finally
                {
                    _free.Enqueue(frame);
                }
            }
        }

        private void StartWorker()
        {
            if (_running) return;
            EnsurePool();

            try
            {
                _client = new UdpClient(_port);
                _client.Client.ReceiveTimeout = ReceiveTimeoutMs;
                _client.Client.ReceiveBufferSize = SocketReceiveBufferBytes;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UdpMotionReceiver] Bind port {_port} failed: {e.Message}");
                _client?.Close();
                _client = null;
                return;
            }

            _running = true;
            _thread = new Thread(ReceiveLoop) { Name = "UdpMotionReceiver", IsBackground = true };
            _thread.Start();
        }

        private void StopWorker()
        {
            if (!_running && _thread == null) return;

            _running = false;
            _client?.Close();
            if (_thread != null && !_thread.Join(JoinTimeoutMs))
                Debug.LogWarning("[UdpMotionReceiver] Receive thread did not exit in time.");
            _thread = null;
            _client = null;

            while (_ready.TryDequeue(out MotionFrame frame)) _free.Enqueue(frame);
        }

        private void ReceiveLoop()
        {
            Socket socket = _client.Client;
            byte[] buffer = _receiveBuffer;
            // Seq of the last accepted packet; 0 = nothing accepted yet (wire seq starts at 1).
            uint lastAccepted = 0;

            while (_running)
            {
                int length;
                try
                {
                    if (!socket.Poll(PollMicroseconds, SelectMode.SelectRead)) continue; // idle: no exception
                    length = socket.Receive(buffer);
                }
                catch (SocketException e)
                {
                    if (!_running) break;
                    switch (e.SocketErrorCode)
                    {
                        case SocketError.TimedOut:
                        case SocketError.ConnectionReset:
                            continue;
                        default: // MessageSize = datagram larger than MaxPacketSize
                            Interlocked.Increment(ref _dropSocket);
                            continue;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (!_free.TryDequeue(out MotionFrame frame))
                {
                    Interlocked.Increment(ref _dropPool);
                    continue;
                }

                if (!MotionPacketSerializer.TryRead(buffer, length, frame))
                {
                    _free.Enqueue(frame);
                    Interlocked.Increment(ref _dropInvalid);
                    continue;
                }

                if (!IsSeqAccepted(frame.Seq, lastAccepted))
                {
                    _free.Enqueue(frame);
                    Interlocked.Increment(ref _dropSeq);
                    continue;
                }

                lastAccepted = frame.Seq;
                _ready.Enqueue(frame);
            }
        }

        /// <summary>
        /// Rejects out-of-order and duplicate packets. Seq 1 is always accepted (sender restarted).
        /// </summary>
        public static bool IsSeqAccepted(uint seq, uint lastAccepted) => seq == 1 || seq > lastAccepted;
    }
}
