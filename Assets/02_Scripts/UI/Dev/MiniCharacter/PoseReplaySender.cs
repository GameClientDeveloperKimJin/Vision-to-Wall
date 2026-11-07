using System.IO;
using System.Net.Sockets;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.Network;

namespace VisionToWall.UI.Dev
{
    /// <summary>
    /// Dev only: plays a pose recording CSV (real MediaPipe PoseLandmarker output, see <see cref="PoseRecording"/>)
    /// to the pose port at its original timing, so the mini character can be checked without anyone in front of
    /// the camera. Do not run together with hand_sensor.py or fake_pose_sender.py on the same port (seq clash).
    /// </summary>
    public sealed class PoseReplaySender : MonoBehaviour
    {
        [Tooltip("Path under Assets/, or an absolute path (e.g. Tools/HandSensor/recordings/pose_*.csv).")]
        [SerializeField] private string csvPath = "02_Scripts/UI/Tests/EditMode/MiniCharacter/Data/pose_arms_lr.csv";
        [SerializeField] private bool loop = true;
        [SerializeField, Tooltip("0 = config udpPosePort")] private int port;
        [SerializeField, Min(0.1f)] private float speed = 1f;

        private readonly PoseFrame _frame = new PoseFrame();
        private readonly byte[] _buffer = new byte[PoseProtocol.PacketSize];
        private PoseRecording _recording;
        private UdpClient _client;
        private float _startTime;
        private int _next;
        private uint _seq;

        public int Sent => (int)_seq;
        public bool IsPlaying => _recording != null;

        private void Start()
        {
            string path = Path.IsPathRooted(csvPath) ? csvPath : Path.Combine(Application.dataPath, csvPath);
            if (!File.Exists(path))
            {
                Debug.LogError($"[PoseReplaySender] Not found: {path}");
                enabled = false;
                return;
            }

            _recording = PoseRecording.Parse(File.ReadAllText(path));
            int target = port != 0 ? port : ConfigService.Current.udpPosePort;
            if (target == 0) target = 9002;
            _client = new UdpClient();
            _client.Connect("127.0.0.1", target);
            _startTime = Time.unscaledTime;
            Debug.Log($"[PoseReplaySender] {Path.GetFileName(path)}: {_recording.Count} samples, {_recording.DurationSec:0.0} s -> 127.0.0.1:{target}");
        }

        private void Update()
        {
            if (_recording == null || _recording.Count == 0) return;
            float t = (Time.unscaledTime - _startTime) * speed;

            while (_next < _recording.Count && _recording[_next].TimestampMs / 1000f <= t)
            {
                _recording.CopyTo(_next, _frame, ++_seq);
                int len = PosePacketSerializer.Write(_frame, _seq, _buffer);
                _client.Send(_buffer, len);
                _next++;
            }

            if (_next >= _recording.Count && loop && t > _recording.DurationSec + 1f)
            {
                _next = 0;
                _startTime = Time.unscaledTime;
            }
        }

        private void OnDestroy() => _client?.Close();
    }
}
