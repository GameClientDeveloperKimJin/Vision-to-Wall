using System;
using UnityEngine;

namespace VisionToWall.Core
{
    /// <summary>Sensor -> Network. Implemented by Network.UdpMotionSender.</summary>
    public interface IMotionFrameSink
    {
        /// <summary>Called on the main thread. Must copy the frame and return without blocking.</summary>
        void Submit(MotionFrame frame);
    }

    /// <summary>Network -> Render/UI. Implemented by Network.UdpMotionReceiver (or a module mock).</summary>
    public interface IPointSource
    {
        /// <summary>Raised on the main thread. The frame is a reused buffer.</summary>
        event Action<MotionFrame> FrameReceived;
    }

    /// <summary>Network -> UI. Implemented by Network.UdpMotionReceiver.</summary>
    public interface INetworkStats
    {
        int PacketsReceived { get; }
        /// <summary>Bad magic, bad length, sequence regression or pool exhaustion.</summary>
        int PacketsDropped { get; }
        uint LastSeq { get; }
        /// <summary>Time.unscaledTime of the last dequeued packet.</summary>
        float LastPacketTime { get; }
    }

    /// <summary>Sensor -> UI. Implemented by Sensor.WebcamMotionSensor.</summary>
    public interface IDebugViewProvider
    {
        /// <summary>Raw webcam image (mirror applied).</summary>
        Texture SourceTexture { get; }
        /// <summary>Grayscale luminance difference at grid resolution.</summary>
        Texture DiffTexture { get; }
        /// <summary>Cells above threshold highlighted.</summary>
        Texture GridTexture { get; }
        int GridWidth { get; }
        int GridHeight { get; }
    }

    /// <summary>Render -> UI. Implemented by Render.ExhibitionStateMachine.</summary>
    public interface IExhibitionStateSource
    {
        ExhibitionState Current { get; }
        /// <summary>Raised on the main thread.</summary>
        event Action<ExhibitionState> StateChanged;
    }
}
