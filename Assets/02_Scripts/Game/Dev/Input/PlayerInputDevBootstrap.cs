using UnityEngine;
using VisionToWall.Core;
using VisionToWall.Network;

namespace VisionToWall.Game.Dev
{
    /// <summary>
    /// Dev scene for <see cref="PlayerInputTracker"/>: position marker, motion bar and source label.
    /// Pair with Tools/HandSensor (this scene never opens the webcam) or Tools/FakeSender/fake_sender.py.
    /// IMGUI allocates; turn the overlay off when profiling GC.
    /// </summary>
    public sealed class PlayerInputDevBootstrap : MonoBehaviour
    {
        [SerializeField, Tooltip("Treat the sensor as external without touching config.json. Off = use config sensorMode.")]
        private bool forceExternal = true;
        [SerializeField] private bool showOverlay = true;

        private const float Pad = 8f;
        private const float LineHeight = 22f;
        private const float BarWidth = 400f;
        private const float BarHeight = 18f;
        private const float MarkerSize = 36f;
        /// <summary>Default shieldThreshold; shown on the bar for the manual check.</summary>
        private const float ShieldLine = 0.8f;
        private static readonly Color BarBg = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color BarFill = new Color(1f, 0.7f, 0.1f, 1f);
        private static readonly Color BodyColor = new Color(0.2f, 1f, 0.4f, 1f);
        private static readonly Color CentroidColor = new Color(0.3f, 0.7f, 1f, 1f);

        private PlayerInputTracker _tracker;
        private UdpMotionReceiver _motion;
        private UdpMotionReceiver _body;
        private UdpPoseReceiver _pose;
        private AppConfig _config;

        private void Awake()
        {
            _config = ConfigService.Load();
            if (forceExternal) _config.sensorMode = AppConfig.SensorModeExternal; // in memory only

            _motion = CreateReceiver("MotionReceiver", _config.udpPort);
            _body = CreateReceiver("BodyReceiver", _config.udpBodyPort);
            _tracker = gameObject.AddComponent<PlayerInputTracker>();
            var poseObject = new GameObject("PoseReceiver");
            poseObject.transform.SetParent(transform, false);
            _pose = poseObject.AddComponent<UdpPoseReceiver>();
            _pose.Initialize(_config); // udpPosePort
            _tracker.Initialize(_config, _motion, _body, _pose);
        }

        private UdpMotionReceiver CreateReceiver(string objectName, int port)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var receiver = child.AddComponent<UdpMotionReceiver>();
            receiver.Initialize(_config, port);
            return receiver;
        }

        private void OnGUI()
        {
            if (!showOverlay || _tracker == null) return;

            PlayerSample s = _tracker.Current;
            Color prev = GUI.color;

            if (s.hasPosition)
            {
                // Origin is bottom-left -> flip for GUI.
                GUI.color = s.source == PlayerPositionSource.Body ? BodyColor : CentroidColor;
                GUI.DrawTexture(new Rect(s.position.x * Screen.width - MarkerSize * 0.5f,
                    (1f - s.position.y) * Screen.height - MarkerSize * 0.5f, MarkerSize, MarkerSize),
                    Texture2D.whiteTexture);
            }

            float y = Pad;
            GUI.color = BarBg;
            GUI.DrawTexture(new Rect(Pad, y, BarWidth, BarHeight), Texture2D.whiteTexture);
            GUI.color = BarFill;
            GUI.DrawTexture(new Rect(Pad, y, BarWidth * s.motion, BarHeight), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(Pad + BarWidth * ShieldLine, y, 2f, BarHeight), Texture2D.whiteTexture);
            y += BarHeight + Pad;

            Line(ref y, $"motion {s.motion:F2}   source {s.source}   hasPosition {s.hasPosition}   guard {s.guardPose}");
            Line(ref y, $"position ({s.position.x:F2}, {s.position.y:F2})   mode {_config.sensorMode}");
            Line(ref y, $"motion :{_config.udpPort} recv {_motion.PacketsReceived} drop {_motion.PacketsDropped}");
            Line(ref y, $"body   :{_config.udpBodyPort} recv {_body.PacketsReceived} drop {_body.PacketsDropped}");
            Line(ref y, $"pose   :{_config.udpPosePort} recv {_pose.PacketsReceived} drop {_pose.PacketsDropped}");
            GUI.color = prev;
        }

        private static void Line(ref float y, string text)
        {
            GUI.Label(new Rect(Pad, y, BarWidth * 2f, LineHeight), text);
            y += LineHeight;
        }
    }
}
