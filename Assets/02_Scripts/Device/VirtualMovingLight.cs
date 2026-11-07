using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Device
{
    /// <summary>
    /// On-screen stand-in for the moving light. It turns by the angles decoded from the last packet of a
    /// <see cref="DeviceController"/>, not by the boss angles, so it shows the same path a real device would take.
    /// It hangs just above the top edge of the camera view and points toward the viewer like the boss's rest pose:
    /// pan + turns to its own right (screen left), tilt + looks up. Packets with a bad checksum are ignored.
    /// No per-frame allocation.
    /// </summary>
    public sealed class VirtualMovingLight : MonoBehaviour
    {
        [SerializeField, Tooltip("Turned by pan/tilt. Its +Z is the beam direction.")]
        private Transform head;
        [SerializeField, Tooltip("Viewport point of the fixture. y > 1 is above the top edge.")]
        private Vector2 viewportPosition = new Vector2(0.5f, 1.06f);
        [SerializeField, Min(0.1f), Tooltip("Distance from the camera, along its axis. Behind the boss (10) so the boss hides the beam.")]
        private float distance = 14f;
        [SerializeField, Range(-90f, 0f), Tooltip("Mount angle added to tilt so the beam falls across the screen.")]
        private float mountTiltDeg = -35f;
        [SerializeField, Min(0.01f), Tooltip("Visual smoothing between packets. 0.01 = follow each packet.")]
        private float smoothTauSec = 0.03f;
        [SerializeField, Tooltip("Layer of the fixture and beam. 1 = TransparentFX, the combat overlay camera layer.")]
        private int layer = 1;

        private DeviceController _source;
        private Camera _camera;
        private Quaternion _rest = Quaternion.identity;
        private Quaternion _goal = Quaternion.identity;
        private int _lastPacketNumber;

        public int DecodedPackets { get; private set; }
        public int RejectedPackets { get; private set; }
        public float PanDeg { get; private set; }
        public float TiltDeg { get; private set; }

        /// <summary>
        /// Places the light for <paramref name="viewCamera"/>. Deactivates itself when the controller is not active
        /// (<c>deviceEnabled: false</c>), so call it after <see cref="DeviceController.Initialize"/>.
        /// </summary>
        public void Initialize(DeviceController source, Camera viewCamera)
        {
            _source = source;
            _camera = viewCamera;

            if (_source == null || !_source.IsActive || _camera == null)
            {
                gameObject.SetActive(false);
                return;
            }

            SetLayerRecursively(transform, layer);
            Place();
            PanDeg = 0f;
            TiltDeg = 0f;
            _lastPacketNumber = 0;
            _goal = _rest * Quaternion.Euler(-mountTiltDeg, 0f, 0f);
            if (head != null) head.rotation = _goal;
            gameObject.SetActive(true);
        }

        /// <summary>Re-reads the camera pose; call after the camera moves or the aspect changes.</summary>
        public void Place()
        {
            if (_camera == null) return;
            Transform cam = _camera.transform;
            transform.position = _camera.ViewportToWorldPoint(new Vector3(viewportPosition.x, viewportPosition.y, distance));
            transform.rotation = cam.rotation;
            // Same rest as the boss: facing the camera along its axis.
            _rest = Quaternion.LookRotation(-cam.forward, cam.up);
        }

        private void LateUpdate()
        {
            if (_source == null || head == null) return;

            Span<byte> packet = stackalloc byte[DeviceProtocol.PacketSize];
            int sent = _source.PacketsSent;
            if (sent != _lastPacketNumber && _source.TryGetLastPacket(packet))
            {
                _lastPacketNumber = sent;
                if (DevicePacketEncoder.TryDecode(packet, out _, out float pan, out float tilt))
                {
                    DecodedPackets++;
                    PanDeg = pan;
                    TiltDeg = tilt;
                    _goal = _rest * Quaternion.Euler(-(tilt + mountTiltDeg), pan, 0f);
                }
                else
                {
                    RejectedPackets++;
                }
            }

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, DeviceController.MaxFrameDeltaSec);
            head.rotation = Quaternion.Slerp(head.rotation, _goal, 1f - Mathf.Exp(-deltaTime / smoothTauSec));
        }

        /// <summary>
        /// Open cone along +Z with its tip at the origin. uv.x goes around, uv.y from tip (0) to end (1).
        /// One-time use by the prefab builder or a missing mesh.
        /// </summary>
        public static Mesh BuildConeMesh(float length, float halfAngleDeg, int segments)
        {
            segments = Mathf.Max(3, segments);
            float radius = length * Mathf.Tan(halfAngleDeg * Mathf.Deg2Rad);
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float a = u * Mathf.PI * 2f;
                var ring = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, length);
                vertices[i * 2] = Vector3.zero;
                vertices[i * 2 + 1] = ring;
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                int t = i * 6;
                int v = i * 2;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 3;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }

            var mesh = new Mesh { name = "DeviceBeamCone" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void SetLayerRecursively(Transform root, int value)
        {
            root.gameObject.layer = value;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), value);
        }
    }
}
