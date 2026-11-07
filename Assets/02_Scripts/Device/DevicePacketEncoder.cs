using System;
using VisionToWall.Core;

namespace VisionToWall.Device
{
    /// <summary>
    /// Pan/tilt angles to and from the 8-byte <see cref="DeviceProtocol"/> packet. Pure functions with no allocation;
    /// the caller owns the buffer. Out of range angles are clamped, NaN is sent as 0.
    /// </summary>
    public static class DevicePacketEncoder
    {
        /// <returns>False only when <paramref name="destination"/> is shorter than <see cref="DeviceProtocol.PacketSize"/>.</returns>
        public static bool TryEncode(Span<byte> destination, byte deviceId, float panDeg, float tiltDeg)
        {
            if (destination.Length < DeviceProtocol.PacketSize) return false;

            short pan = ToCentiDegrees(panDeg, DeviceProtocol.PanMinDeg, DeviceProtocol.PanMaxDeg);
            short tilt = ToCentiDegrees(tiltDeg, DeviceProtocol.TiltMinDeg, DeviceProtocol.TiltMaxDeg);

            destination[0] = DeviceProtocol.Stx;
            destination[DeviceProtocol.IdIndex] = deviceId;
            destination[DeviceProtocol.PanIndex] = (byte)(pan >> 8);
            destination[DeviceProtocol.PanIndex + 1] = (byte)pan;
            destination[DeviceProtocol.TiltIndex] = (byte)(tilt >> 8);
            destination[DeviceProtocol.TiltIndex + 1] = (byte)tilt;
            destination[DeviceProtocol.ChecksumIndex] = Checksum(destination);
            destination[DeviceProtocol.EtxIndex] = DeviceProtocol.Etx;
            return true;
        }

        /// <summary>Rejects a wrong length, STX, ETX or checksum, and angles outside the protocol range.</summary>
        public static bool TryDecode(ReadOnlySpan<byte> packet, out byte deviceId, out float panDeg, out float tiltDeg)
        {
            deviceId = 0;
            panDeg = 0f;
            tiltDeg = 0f;

            if (packet.Length != DeviceProtocol.PacketSize) return false;
            if (packet[0] != DeviceProtocol.Stx || packet[DeviceProtocol.EtxIndex] != DeviceProtocol.Etx) return false;
            if (packet[DeviceProtocol.ChecksumIndex] != Checksum(packet)) return false;

            short pan = (short)((packet[DeviceProtocol.PanIndex] << 8) | packet[DeviceProtocol.PanIndex + 1]);
            short tilt = (short)((packet[DeviceProtocol.TiltIndex] << 8) | packet[DeviceProtocol.TiltIndex + 1]);
            float p = pan / DeviceProtocol.CentiDegreesPerDegree;
            float t = tilt / DeviceProtocol.CentiDegreesPerDegree;
            if (p < DeviceProtocol.PanMinDeg || p > DeviceProtocol.PanMaxDeg) return false;
            if (t < DeviceProtocol.TiltMinDeg || t > DeviceProtocol.TiltMaxDeg) return false;

            deviceId = packet[DeviceProtocol.IdIndex];
            panDeg = p;
            tiltDeg = t;
            return true;
        }

        /// <summary>XOR of bytes 1..5 (id, pan, tilt).</summary>
        public static byte Checksum(ReadOnlySpan<byte> packet)
        {
            byte sum = 0;
            for (int i = DeviceProtocol.IdIndex; i < DeviceProtocol.ChecksumIndex; i++) sum ^= packet[i];
            return sum;
        }

        private static short ToCentiDegrees(float degrees, float min, float max)
        {
            if (float.IsNaN(degrees)) degrees = 0f;
            if (degrees < min) degrees = min;
            else if (degrees > max) degrees = max;
            return (short)Math.Round(degrees * DeviceProtocol.CentiDegreesPerDegree, MidpointRounding.AwayFromZero);
        }
    }
}
