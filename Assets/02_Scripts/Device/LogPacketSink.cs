using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Device
{
    /// <summary>
    /// Simulated device: keeps the last packet, counts packets and optionally logs them as hex.
    /// The log is limited to one line per second; only that line allocates (string + Debug.Log).
    /// </summary>
    public sealed class LogPacketSink : IDevicePacketSink
    {
        private const float LogIntervalSec = 1f;
        private const string HexDigits = "0123456789ABCDEF";

        private readonly byte[] _last = new byte[DeviceProtocol.PacketSize];
        private readonly char[] _hex = new char[DeviceProtocol.PacketSize * 3 - 1];
        private float _nextLogTime = float.NegativeInfinity;

        public LogPacketSink(bool logPackets)
        {
            LogPackets = logPackets;
        }

        public bool LogPackets { get; set; }
        public int Count { get; private set; }
        /// <summary>Packets that were not <see cref="DeviceProtocol.PacketSize"/> bytes long; not stored.</summary>
        public int Rejected { get; private set; }
        public bool HasPacket => Count > 0;

        public void Send(ReadOnlySpan<byte> packet)
        {
            if (packet.Length != DeviceProtocol.PacketSize)
            {
                Rejected++;
                return;
            }

            packet.CopyTo(_last);
            Count++;

            if (!LogPackets) return;
            float now = Time.unscaledTime;
            if (now < _nextLogTime) return;
            _nextLogTime = now + LogIntervalSec;
            Debug.Log($"[Device] #{Count} {FormatHex(_last, _hex)}");
        }

        /// <returns>False until the first packet, or when <paramref name="destination"/> is too short.</returns>
        public bool TryGetLastPacket(Span<byte> destination)
        {
            if (!HasPacket || destination.Length < DeviceProtocol.PacketSize) return false;
            _last.CopyTo(destination);
            return true;
        }

        /// <summary>"02 01 F5 10 04 D2 32 03". Allocates the returned string.</summary>
        public static string FormatHex(ReadOnlySpan<byte> packet)
        {
            if (packet.Length == 0) return string.Empty;
            var buffer = new char[packet.Length * 3 - 1];
            return FormatHex(packet, buffer);
        }

        private static string FormatHex(ReadOnlySpan<byte> packet, char[] buffer)
        {
            for (int i = 0; i < packet.Length; i++)
            {
                int c = i * 3;
                buffer[c] = HexDigits[packet[i] >> 4];
                buffer[c + 1] = HexDigits[packet[i] & 0xF];
                if (c + 2 < buffer.Length) buffer[c + 2] = ' ';
            }
            return new string(buffer, 0, packet.Length * 3 - 1);
        }
    }
}
