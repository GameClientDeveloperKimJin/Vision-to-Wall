using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Pure input logic behind <see cref="PlayerInputTracker"/>. Position comes from the body stream, then the
    /// motion centroid, then the last known position for <see cref="HoldSec"/>. Motion intensity only uses the
    /// motion stream, so moving the body does not count as waving the hands. <see cref="PlayerSample.guardPose"/>
    /// comes from the pose stream only (see <see cref="AddPoseFrame"/>); without it, it stays false.
    /// </summary>
    public sealed class PlayerInputFilter
    {
        /// <summary>A stream without a usable packet for longer than this counts as absent.</summary>
        public const float SourceFreshSec = 0.3f;
        /// <summary>How long the last position stays valid after every source is gone.</summary>
        public const float HoldSec = 1f;
        public const float PositionTauSec = 0.1f;
        public const float MotionDecayTauSec = 0.25f;
        /// <summary>Same reference as MediaWallController.activitySaturation.</summary>
        public const float WebcamActivitySaturation = 8f;

        /// <summary>A wrist below this visibility never counts as raised (off-frame arms are filtered by the setup).</summary>
        public const float GuardMinVisibility = 0.5f;
        /// <summary>A raised arm stays raised until the wrist drops this far below its shoulder (screen units).</summary>
        public const float GuardExitMargin = 0.03f;
        /// <summary>Consecutive raised pose frames needed to turn guardPose on. 2 at 30 Hz = about 33 ms.</summary>
        public const int GuardRiseFrames = 2;
        /// <summary>guardPose stays on this long after the last raised frame, so a short dropout does not re-trigger.</summary>
        public const float GuardFallHoldSec = 0.15f;

        private readonly bool _useExternalSensor;

        private Vector2 _bodyPosition;
        private float _bodyTime = float.NegativeInfinity;
        private Vector2 _centroid;
        private float _centroidTime = float.NegativeInfinity;
        private float _motionPacketTime = float.NegativeInfinity;
        private float _lastSeenTime;
        private float _motionTarget;
        private bool _leftArmUp;
        private bool _rightArmUp;
        private int _guardRiseCount;
        private float _guardLastRaisedTime = float.NegativeInfinity;
        private PlayerSample _current;

        public PlayerInputFilter(bool useExternalSensor)
        {
            _useExternalSensor = useExternalSensor;
        }

        public PlayerSample Current => _current;

        public void AddBodyFrame(MotionFrame frame, float now)
        {
            if (frame.Count < 1) return; // nobody: let the last valid packet go stale
            MotionPoint p = frame.Points[0];
            _bodyPosition = new Vector2(p.x, p.y);
            _bodyTime = now;
        }

        public void AddMotionFrame(MotionFrame frame, float now)
        {
            float sumV = 0f;
            float maxV = 0f;
            float sumX = 0f;
            float sumY = 0f;
            for (int i = 0; i < frame.Count; i++)
            {
                MotionPoint p = frame.Points[i];
                sumV += p.v;
                sumX += p.x * p.v;
                sumY += p.y * p.v;
                if (p.v > maxV) maxV = p.v;
            }

            if (sumV > 0f)
            {
                _centroid = new Vector2(sumX / sumV, sumY / sumV);
                _centroidTime = now;
            }

            _motionPacketTime = now;
            _motionTarget = Mathf.Clamp01(_useExternalSensor ? maxV : sumV / WebcamActivitySaturation);
            // Rise here, not in Tick: several frames can arrive per Update and a peak must not be lost.
            if (_motionTarget > _current.motion) _current.motion = _motionTarget;
        }

        /// <summary>
        /// Arm raised = a wrist above its own shoulder on screen with visibility at least
        /// <see cref="GuardMinVisibility"/>. The frame is not kept.
        /// </summary>
        public void AddPoseFrame(PoseFrame frame, float now)
        {
            if (frame == null || !frame.HasPerson)
            {
                // Nobody in view: drop at once instead of waiting for the hold.
                _leftArmUp = _rightArmUp = false;
                _guardRiseCount = 0;
                _guardLastRaisedTime = float.NegativeInfinity;
                _current.guardPose = false;
                return;
            }

            _leftArmUp = IsArmUp(frame, PoseJoint.LeftWrist, PoseJoint.LeftShoulder, _leftArmUp);
            _rightArmUp = IsArmUp(frame, PoseJoint.RightWrist, PoseJoint.RightShoulder, _rightArmUp);
            if (!_leftArmUp && !_rightArmUp)
            {
                _guardRiseCount = 0;
                return; // the hold in Tick lowers guardPose
            }

            _guardLastRaisedTime = now;
            if (_guardRiseCount < GuardRiseFrames) _guardRiseCount++;
            if (_guardRiseCount >= GuardRiseFrames) _current.guardPose = true;
        }

        private static bool IsArmUp(PoseFrame frame, PoseJoint wrist, PoseJoint shoulder, bool wasUp)
        {
            int w = (int)wrist;
            if (frame.Visibility[w] < GuardMinVisibility) return false;
            float above = frame.Screen[w].y - frame.Screen[(int)shoulder].y; // origin bottom-left: up is +y
            return wasUp ? above > -GuardExitMargin : above > 0f;
        }

        /// <summary>Call once per Update, after the frames of this Update were added.</summary>
        public PlayerSample Tick(float now, float deltaTime)
        {
            UpdatePosition(now, deltaTime);
            UpdateMotion(now, deltaTime);
            // Also ends the guard when the pose stream stops (no count-0 packet from a dead sensor).
            if (_current.guardPose && now - _guardLastRaisedTime > GuardFallHoldSec) _current.guardPose = false;
            return _current;
        }

        private void UpdatePosition(float now, float deltaTime)
        {
            PlayerPositionSource source = PlayerPositionSource.None;
            Vector2 target = default;
            if (now - _bodyTime <= SourceFreshSec)
            {
                source = PlayerPositionSource.Body;
                target = _bodyPosition;
            }
            else if (now - _centroidTime <= SourceFreshSec)
            {
                source = PlayerPositionSource.MotionCentroid;
                target = _centroid;
            }

            if (source != PlayerPositionSource.None)
            {
                // Smoothing continues across a source change, so the position does not jump.
                _current.position = _current.hasPosition
                    ? Vector2.Lerp(_current.position, target, 1f - Mathf.Exp(-deltaTime / PositionTauSec))
                    : target;
                _current.hasPosition = true;
                _current.source = source;
                _lastSeenTime = now;
            }
            else if (_current.hasPosition && now - _lastSeenTime > HoldSec)
            {
                // During the hold, source keeps naming where the held position came from.
                _current.hasPosition = false;
                _current.source = PlayerPositionSource.None;
            }
        }

        private void UpdateMotion(float now, float deltaTime)
        {
            // A dead sensor sends no count-0 packet, so the last target must expire by itself.
            float target = now - _motionPacketTime <= SourceFreshSec ? _motionTarget : 0f;
            if (target >= _current.motion)
                _current.motion = target;
            else
                _current.motion = target + (_current.motion - target) * Mathf.Exp(-deltaTime / MotionDecayTauSec);
        }
    }
}
