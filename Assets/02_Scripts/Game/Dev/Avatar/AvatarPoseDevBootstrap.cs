using UnityEngine;
using UnityEngine.InputSystem;
using VisionToWall.Core;
using VisionToWall.Network;

namespace VisionToWall.Game.Dev
{
    /// <summary>
    /// R4 Dev scene (<c>AvatarPoseDev.unity</c>, a copy of PresentationDev): opens the pose receiver
    /// (<c>udpPosePort</c>, 9002) and hands it to the combat avatar, the way <c>AppBootstrap</c> will in Main.
    /// Feed it with <c>Tools/HandSensor/fake_pose_sender.py --pattern wave|raise|jacks</c> or the real hand sensor.
    /// P toggles the pose source (off = rest pose, as before R4). The overlay allocates; turn it off when profiling.
    /// </summary>
    public sealed class AvatarPoseDevBootstrap : MonoBehaviour
    {
        [SerializeField] private PlayerAvatarView avatar;
        [SerializeField] private bool poseOn = true;
        [SerializeField] private bool showOverlay = true;

        private UdpPoseReceiver _receiver;

        public UdpPoseReceiver Receiver => _receiver;

        private void Start()
        {
            AppConfig config = ConfigService.Load();
            var child = new GameObject("PoseReceiver");
            child.transform.SetParent(transform, false);
            _receiver = child.AddComponent<UdpPoseReceiver>();
            _receiver.Initialize(config); // udpPosePort; the Dev scene ignores sensorMode, like forceExternal
            Apply();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
            {
                poseOn = !poseOn;
                Apply();
            }
        }

        private void Apply()
        {
            if (avatar != null) avatar.SetPoseSource(poseOn ? _receiver : null);
        }

        private void OnGUI()
        {
            if (!showOverlay || avatar == null || _receiver == null) return;

            AvatarPoseSolver p = avatar.Pose;
            GUI.Label(new Rect(8f, 74f, 1200f, 22f),
                $"pose {(poseOn ? "on" : "off")} (P)  port {_receiver.Port} packets {_receiver.PacketsReceived} empty {_receiver.EmptyPacketsReceived} drop {_receiver.PacketsDropped}  " +
                $"present {p.Present} torso {p.TorsoWeight:F2}  arms L {p.Weight(AvatarLimb.LeftUpperArm):F2} R {p.Weight(AvatarLimb.RightUpperArm):F2}  legs L {p.Weight(AvatarLimb.LeftUpperLeg):F2} R {p.Weight(AvatarLimb.RightUpperLeg):F2}");
        }
    }
}
