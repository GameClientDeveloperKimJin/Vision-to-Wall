using UnityEngine;

namespace VisionToWall.Device
{
    /// <summary>
    /// Timing and motion state of <see cref="DeviceController"/>, kept free of Unity time so it can be tested.
    /// The output angle follows the target at no more than <see cref="MaxDegPerSec"/> to protect the device.
    /// A send is due every 1 / <see cref="SendHz"/> seconds; after a stall it resumes on the next period instead of
    /// sending the missed packets in a burst.
    /// </summary>
    public sealed class DeviceSendLoop
    {
        public const int MinSendHz = 1;
        public const int MaxSendHz = 60;

        private float _nextSendTime = float.NegativeInfinity;

        public DeviceSendLoop(int sendHz, float maxDegPerSec)
        {
            SendHz = Mathf.Clamp(sendHz, MinSendHz, MaxSendHz);
            MaxDegPerSec = Mathf.Max(0f, maxDegPerSec);
        }

        public int SendHz { get; }
        public float PeriodSec => 1f / SendHz;
        public float MaxDegPerSec { get; set; }
        public float PanDeg { get; private set; }
        public float TiltDeg { get; private set; }

        /// <param name="now">Unscaled time in seconds.</param>
        /// <param name="deltaTime">Unscaled and limited by the caller.</param>
        /// <returns>True when a packet should be sent with <see cref="PanDeg"/> and <see cref="TiltDeg"/>.</returns>
        public bool Step(float now, float deltaTime, float targetPanDeg, float targetTiltDeg)
        {
            if (float.IsNaN(targetPanDeg)) targetPanDeg = 0f;
            if (float.IsNaN(targetTiltDeg)) targetTiltDeg = 0f;

            float maxStep = MaxDegPerSec * Mathf.Max(0f, deltaTime);
            PanDeg = Mathf.MoveTowards(PanDeg, targetPanDeg, maxStep);
            TiltDeg = Mathf.MoveTowards(TiltDeg, targetTiltDeg, maxStep);

            if (now < _nextSendTime) return false;
            _nextSendTime += PeriodSec;
            if (_nextSendTime <= now) _nextSendTime = now + PeriodSec; // first send or a stall
            return true;
        }

        /// <summary>Back to 0 degrees at once with the next step sending. Use only when the device is known to be at rest.</summary>
        public void Reset()
        {
            PanDeg = 0f;
            TiltDeg = 0f;
            _nextSendTime = float.NegativeInfinity;
        }
    }
}
