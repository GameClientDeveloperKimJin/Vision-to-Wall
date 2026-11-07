using System;

namespace VisionToWall.Device
{
    /// <summary>
    /// Where encoded packets go. <see cref="LogPacketSink"/> simulates the device; a serial port sink can replace it
    /// later without touching packet creation. Called on the main thread; must not keep <paramref name="packet"/>.
    /// </summary>
    public interface IDevicePacketSink
    {
        void Send(ReadOnlySpan<byte> packet);
    }
}
