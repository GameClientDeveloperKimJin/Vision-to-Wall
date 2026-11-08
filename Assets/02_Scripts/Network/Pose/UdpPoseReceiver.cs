using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Network
{
    /// <summary>
    /// Pose joint stream receiver (UDP <c>udpPosePort</c>, default 9002). Same design as <see cref="UdpMotionReceiver"/>:
    /// a background thread reads into a fixed buffer, parses into pooled frames and queues them; <see cref="Update"/>
    /// copies each into <see cref="Latest"/> and raises <see cref="PoseReceived"/> on the main thread.
    /// Port 0 = off (no socket). The sender sends nothing until someone is first seen, then count 0 once on loss.
    /// </summary>
    public sealed class UdpPoseReceiver : MonoBehaviour, IPoseSource, INetworkStats
    {
        private const int PoolSize = 8;
        private const int MaxDispatchPerFrame = PoolSize;
        private const int ReceiveTimeoutMs = 500;
        /// <summary>
        /// Wait for a datagram this long before re-checking the stop flag. Poll returns false on an idle socket instead
        /// of the SocketException a ReceiveTimeout throws (about 4 KB of garbage each, twice a second per idle port).
        /// </summary>
        private const int PollMicroseconds = ReceiveTimeoutMs * 1000;
        private const int JoinTimeoutMs = 1000;
        /// <summary>OS receive buffer: ~200 packets of headroom for main-thread hitches.</summary>
        private const int SocketReceiveBufferBytes = 1 << 16;
        /// <summary>Larger than a valid packet so oversized datagrams are seen as invalid instead of truncated.</summary>
        private const int ReceiveBufferSize = PoseProtocol.PacketSize + 64;

        public event Action<PoseFrame> PoseReceived;

        private readonly ConcurrentQueue<PoseFrame> _free = new ConcurrentQueue<PoseFrame>();
        private readonly ConcurrentQueue<PoseFrame> _ready = new ConcurrentQueue<PoseFrame>();
        private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];
        private readonly PoseFrame _latest = new PoseFrame();

        private AppConfig _config;
        private int _port;
        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;
        private bool _poolReady;

        private int _packetsReceived;
        private int _emptyReceived;
        private int _dropPool;
        private int _dropInvalid;
        private int _dropSeq;
        private int _dropSocket;
        private uint _lastSeq;
        private float _lastPacketTime = -1f;

        public PoseFrame Latest => _latest;
        public float LastPoseTime => _lastPacketTime;

        public int PacketsReceived => _packetsReceived;
        /// <summary>Received packets with count 0 (nobody in view). Included in <see cref="PacketsReceived"/>.</summary>
        public int EmptyPacketsReceived => _emptyReceived;
        public int PacketsDropped => DroppedPoolExhausted + DroppedInvalid + DroppedSeq + DroppedSocket;
        /// <summary>
        /// No free frame: main thread is not dispatching fast enough (hitch, unfocused editor). The oldest queued
        /// frame is the one dropped, so the newest pose always gets through.
        /// </summary>
        public int DroppedPoolExhausted => Volatile.Read(ref _dropPool);
        /// <summary>Bad magic, length or count.</summary>
        public int DroppedInvalid => Volatile.Read(ref _dropInvalid);
        /// <summary>Seq regression or duplicate (e.g. two senders on one port).</summary>
        public int DroppedSeq => Volatile.Read(ref _dropSeq);
        /// <summary>Socket error other than timeout.</summary>
        public int DroppedSocket => Volatile.Read(ref _dropSocket);
        public uint LastSeq => _lastSeq;
        public float LastPacketTime => _lastPacketTime;
        public bool IsRunning => _running;
        public int Port => _port;

        /// <summary>Uses <c>udpPosePort</c> (0 = off). Restart required to change it.</summary>
        public void Initialize(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Initialize(config, config.udpPosePort);
        }

        /// <summary>Binds <paramref name="port"/>; 0 leaves the receiver closed.</summary>
        public void Initialize(AppConfig config, int port)
        {
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _port = port;
            StopWorker();
            if (isActiveAndEnabled) StartWorker();
        }

        private void EnsurePool()
        {
            if (_poolReady) return;
            _poolReady = true;
            for (int i = 0; i < PoolSize; i++) _free.Enqueue(new PoseFrame());
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
                if (!_ready.TryDequeue(out PoseFrame frame)) break;

                _packetsReceived++;
                if (!frame.HasPerson) _emptyReceived++;
                _lastSeq = frame.Seq;
                _lastPacketTime = Time.unscaledTime;
                _latest.CopyFrom(frame);
                _free.Enqueue(frame);

                PoseReceived?.Invoke(_latest);
            }
        }

        private void StartWorker()
        {
            if (_running || _port == 0) return;
            EnsurePool();

            try
            {
                _client = new UdpClient(_port);
                _client.Client.ReceiveTimeout = ReceiveTimeoutMs;
                _client.Client.ReceiveBufferSize = SocketReceiveBufferBytes;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UdpPoseReceiver] Bind port {_port} failed: {e.Message}");
                _client?.Close();
                _client = null;
                return;
            }

            _running = true;
            _thread = new Thread(ReceiveLoop) { Name = "UdpPoseReceiver", IsBackground = true };
            _thread.Start();
        }

        private void StopWorker()
        {
            if (!_running && _thread == null) return;

            _running = false;
            _client?.Close();
            if (_thread != null && !_thread.Join(JoinTimeoutMs))
                Debug.LogWarning("[UdpPoseReceiver] Receive thread did not exit in time.");
            _thread = null;
            _client = null;

            while (_ready.TryDequeue(out PoseFrame frame)) _free.Enqueue(frame);
        }

        private void ReceiveLoop()
        {
            Socket socket = _client.Client;
            byte[] buffer = _receiveBuffer;
            uint lastAccepted = 0; // 0 = nothing accepted yet (wire seq starts at 1)

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
                        case SocketError.MessageSize:
                            Interlocked.Increment(ref _dropInvalid);
                            continue;
                        default:
                            Interlocked.Increment(ref _dropSocket);
                            continue;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                // Latest wins: when the main thread stalls, the oldest queued frame is dropped and reused,
                // so the figure resumes from the newest pose instead of replaying stale ones.
                if (!_free.TryDequeue(out PoseFrame frame))
                {
                    Interlocked.Increment(ref _dropPool);
                    if (!_ready.TryDequeue(out frame)) continue;
                }

                if (!PosePacketSerializer.TryRead(buffer, length, frame))
                {
                    _free.Enqueue(frame);
                    Interlocked.Increment(ref _dropInvalid);
                    continue;
                }

                if (!UdpMotionReceiver.IsSeqAccepted(frame.Seq, lastAccepted))
                {
                    _free.Enqueue(frame);
                    Interlocked.Increment(ref _dropSeq);
                    continue;
                }

                lastAccepted = frame.Seq;
                _ready.Enqueue(frame);
            }
        }
    }
}
