using System.Collections.Generic;
using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Player figure made of glowing points. Samples a source mesh once into a quad per point; the shader faces
    /// each quad to the camera. The figure moves as one with the judged position; limbs do not follow real poses.
    /// Its size comes from the hit radius, so what looks like a hit is one. The source mesh needs Read/Write.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlayerAvatarView : MonoBehaviour
    {
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int BackCenterXId = Shader.PropertyToID("_BackCenterX");

        [SerializeField, Tooltip("Needs Read/Write.")]
        private Mesh sourceMesh;
        [SerializeField, Tooltip("Baked into the points so the figure's front is +Z and up is +Y.")]
        private Vector3 meshEuler = new Vector3(270f, 0f, 0f);
        [SerializeField, Range(64, 40000)] private int pointCount = 6000;
        [SerializeField, Range(0.1f, 1f), Tooltip("Torso width over the mesh's full width. Arms out = small value.")]
        private float torsoOverBounds = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("Height on the mesh placed at the judged position. 0 = feet, 1 = head.")]
        private float anchorHeight = 0.6f;
        [SerializeField, Min(0.01f)] private float flashFadeSec = 0.4f;
        [SerializeField, Range(0f, 90f), Tooltip("Turn toward the boss at the screen edge. The boss is at screen center.")]
        private float maxFacingYawDeg = 35f;
        [SerializeField, Min(0.01f)] private float facingSmoothSec = 0.2f;

        private BossCombatController _combat;
        private MeshRenderer _renderer;
        private Material _material;
        private Mesh _pointMesh;
        private Bounds _bounds;
        private float _scale = 1f;
        private float _anchorY;
        private float _flash;
        private float _yaw;

        public bool HasPoints => _pointMesh != null;

        /// <summary>World width of the torso at the player plane.</summary>
        public float TorsoWorldWidth { get; private set; }

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _material = _renderer.material; // per-instance copy, destroyed in OnDestroy
            _renderer.enabled = false;
            Rebuild();
            UpdateScale(); // Bind may have run first
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_pointMesh != null) Destroy(_pointMesh);
        }

        public void Bind(BossCombatController combat, float hitRadius)
        {
            _combat = combat;
            float screenHeight = WallSpace.ScreenHeightAt(combat.OverlayCamera, combat.PlayerPlaneDistance);
            TorsoWorldWidth = hitRadius * 2f * screenHeight;
            UpdateScale();
        }

        /// <summary>Swaps the model. Allocates; call at setup only.</summary>
        public void SetMesh(Mesh mesh, float torsoRatio)
        {
            sourceMesh = mesh;
            torsoOverBounds = Mathf.Clamp(torsoRatio, 0.1f, 1f);
            Rebuild();
            UpdateScale();
        }

        public void SetVisible(bool visible)
        {
            if (_renderer != null) _renderer.enabled = visible && _pointMesh != null;
        }

        public void Flash()
        {
            _flash = 1f;
            if (_material != null) _material.SetFloat(FlashId, 1f);
        }

        private void LateUpdate()
        {
            if (_combat == null || _renderer == null || !_renderer.enabled) return;

            Transform cam = _combat.OverlayCamera.transform;
            float frameDelta = Mathf.Min(Time.unscaledDeltaTime, GamePhaseMachine.MaxFrameDeltaSec);
            float targetYaw = FacingYawDeg(_combat.PlayerPosition.x, maxFacingYawDeg);
            _yaw = Mathf.Lerp(_yaw, targetYaw, 1f - Mathf.Exp(-frameDelta / facingSmoothSec));
            // Back to the camera, turned toward the boss.
            Quaternion rotation = Quaternion.LookRotation(cam.forward, cam.up) * Quaternion.Euler(0f, _yaw, 0f);
            Vector3 anchor = WallSpace.ToWorld(_combat.OverlayCamera, _combat.PlayerPosition,
                _combat.PlayerPlaneDistance);
            transform.SetPositionAndRotation(anchor - rotation * new Vector3(0f, _anchorY * _scale, 0f), rotation);

            if (_flash > 0f)
            {
                float deltaTime = Mathf.Min(Time.unscaledDeltaTime, GamePhaseMachine.MaxFrameDeltaSec);
                _flash = Mathf.MoveTowards(_flash, 0f, deltaTime / flashFadeSec);
                _material.SetFloat(FlashId, _flash);
            }
        }

        /// <summary>Positive turns the figure's front to screen right. 0 at screen center, where the boss is.</summary>
        public static float FacingYawDeg(float playerX01, float maxYawDeg) =>
            Mathf.Clamp((0.5f - playerX01) * 2f, -1f, 1f) * maxYawDeg;

        private void UpdateScale()
        {
            if (_pointMesh == null || TorsoWorldWidth <= 0f) return;

            float torso = Mathf.Max(_bounds.size.x * torsoOverBounds, 1e-4f);
            _scale = TorsoWorldWidth / torso;
            _anchorY = Mathf.Lerp(_bounds.min.y, _bounds.max.y, anchorHeight);
            transform.localScale = Vector3.one * _scale;
        }

        private void Rebuild()
        {
            if (_pointMesh != null) Destroy(_pointMesh);
            _pointMesh = null;

            if (sourceMesh == null) return;
            if (!sourceMesh.isReadable)
            {
                Debug.LogError($"[Presentation] Avatar mesh {sourceMesh.name} needs Read/Write enabled.", this);
                return;
            }

            _pointMesh = BuildPointMesh(sourceMesh, pointCount, Quaternion.Euler(meshEuler));
            _bounds = _pointMesh.bounds;
            if (_material != null) _material.SetFloat(BackCenterXId, _bounds.center.x);
            Bounds culling = _bounds;
            culling.Expand(_bounds.size.magnitude * 0.05f); // quads grow in the vertex shader
            _pointMesh.bounds = culling;
            GetComponent<MeshFilter>().sharedMesh = _pointMesh;
        }

        /// <summary>Area-weighted surface samples, so density does not depend on how the model was triangulated.</summary>
        /// <param name="rotation">Applied to every sample. The returned mesh's bounds fit the samples tightly.</param>
        public static Mesh BuildPointMesh(Mesh source, int points, Quaternion rotation)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            int[] triangles = source.triangles;
            int triangleCount = triangles.Length / 3;

            var cumulative = new float[triangleCount];
            float total = 0f;
            for (int t = 0; t < triangleCount; t++)
            {
                Vector3 a = vertices[triangles[t * 3]];
                Vector3 b = vertices[triangles[t * 3 + 1]];
                Vector3 c = vertices[triangles[t * 3 + 2]];
                total += Vector3.Cross(b - a, c - a).magnitude;
                cumulative[t] = total;
            }

            var positions = new List<Vector3>(points * 4);
            var pointNormals = new List<Vector3>(points * 4);
            var corners = new List<Vector3>(points * 4); // xy = quad corner, z = per-point random
            var indices = new int[points * 6];
            var random = new System.Random(20261106); // same figure every run
            bool hasNormals = normals != null && normals.Length == vertices.Length;

            for (int p = 0; p < points; p++)
            {
                int t = System.Array.BinarySearch(cumulative, (float)random.NextDouble() * total);
                if (t < 0) t = ~t;
                t = Mathf.Min(t, triangleCount - 1);

                int ia = triangles[t * 3];
                int ib = triangles[t * 3 + 1];
                int ic = triangles[t * 3 + 2];
                float u = Mathf.Sqrt((float)random.NextDouble());
                float v = (float)random.NextDouble();
                float wa = 1f - u;
                float wb = u * (1f - v);
                float wc = u * v;

                Vector3 position = rotation * (vertices[ia] * wa + vertices[ib] * wb + vertices[ic] * wc);
                Vector3 normal = hasNormals
                    ? rotation * (normals[ia] * wa + normals[ib] * wb + normals[ic] * wc).normalized
                    : Vector3.up;
                float seed = (float)random.NextDouble();

                int first = p * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    positions.Add(position);
                    pointNormals.Add(normal);
                }
                corners.Add(new Vector3(-1f, -1f, seed));
                corners.Add(new Vector3(1f, -1f, seed));
                corners.Add(new Vector3(1f, 1f, seed));
                corners.Add(new Vector3(-1f, 1f, seed));

                int index = p * 6;
                indices[index] = first;
                indices[index + 1] = first + 1;
                indices[index + 2] = first + 2;
                indices[index + 3] = first;
                indices[index + 4] = first + 2;
                indices[index + 5] = first + 3;
            }

            var mesh = new Mesh { name = source.name + " Points" };
            if (positions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(pointNormals);
            mesh.SetUVs(0, corners);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
