using System;
using UnityEngine;

namespace VisionToWall.Core
{
    /// <summary>
    /// Reusable pose frame buffer. Receivers must not keep a reference outside the event callback;
    /// call <see cref="CopyFrom"/> into their own frame instead. Indexed by <see cref="PoseJoint"/>.
    /// </summary>
    public sealed class PoseFrame
    {
        /// <summary>0..1, origin bottom-left, mirror applied.</summary>
        public readonly Vector2[] Screen = new Vector2[PoseProtocol.JointCount];
        /// <summary>Meters, hip centered, Unity axes (+x right, +y up, +z away from the camera).</summary>
        public readonly Vector3[] World = new Vector3[PoseProtocol.JointCount];
        /// <summary>0..1 from MediaPipe.</summary>
        public readonly float[] Visibility = new float[PoseProtocol.JointCount];

        /// <summary>False when the packet said nobody is in view (count 0).</summary>
        public bool HasPerson;
        public uint Seq;
        public uint TimestampMs;

        public void Clear()
        {
            HasPerson = false;
            Seq = 0;
            TimestampMs = 0;
            Array.Clear(Screen, 0, Screen.Length);
            Array.Clear(World, 0, World.Length);
            Array.Clear(Visibility, 0, Visibility.Length);
        }

        public void CopyFrom(PoseFrame other)
        {
            if (other == null)
            {
                Clear();
                return;
            }

            HasPerson = other.HasPerson;
            Seq = other.Seq;
            TimestampMs = other.TimestampMs;
            Array.Copy(other.Screen, Screen, Screen.Length);
            Array.Copy(other.World, World, World.Length);
            Array.Copy(other.Visibility, Visibility, Visibility.Length);
        }
    }

    /// <summary>Network -> UI/Game. Implemented by Network.UdpPoseReceiver (R3) or a module mock.</summary>
    public interface IPoseSource
    {
        /// <summary>Raised on the main thread. The frame is a reused buffer.</summary>
        event Action<PoseFrame> PoseReceived;

        /// <summary>Last frame on the main thread; HasPerson false before the first packet.</summary>
        PoseFrame Latest { get; }

        /// <summary>Time.unscaledTime of the last packet, or a negative value before the first.</summary>
        float LastPoseTime { get; }
    }
}
