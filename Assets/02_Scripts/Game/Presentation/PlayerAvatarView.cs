using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Player figure made of glowing points. Samples a source mesh once into a quad per point; the shader faces
    /// each quad to the camera. The figure is placed at the judged position and turned toward the boss; its size
    /// comes from the hit radius, so what looks like a hit is one. Judgement never reads the pose.
    /// <para>
    /// With <see cref="rigModel"/> (R4) the points are a skinned mesh on the rig's bones, and
    /// <see cref="SetPoseSource"/> makes the limbs and torso follow the player's joints (<see cref="AvatarPoseSolver"/>,
    /// <see cref="AvatarSkeleton"/>). Without a pose source, with nobody in view, or for joints out of view, the
    /// figure is in its rest pose, identical to the static figure. Without <see cref="rigModel"/> the figure is the
    /// static point mesh of <see cref="sourceMesh"/>. Source meshes need Read/Write.
    /// </para>
    /// Points over the boss eye on screen are dimmed (<see cref="bossDimRadius"/>), so a raised arm does not hide it.
    /// Look only: size, position and judgement are unchanged.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlayerAvatarView : MonoBehaviour
    {
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int BackCenterXId = Shader.PropertyToID("_BackCenterX");
        private static readonly int BossViewportId = Shader.PropertyToID("_BossViewport");
        private static readonly int BossFadeRadiusId = Shader.PropertyToID("_BossFadeRadius");
        private static readonly int BossFadeSoftId = Shader.PropertyToID("_BossFadeSoft");
        private static readonly int BossFadeMinId = Shader.PropertyToID("_BossFadeMin");

        [SerializeField, Tooltip("Static figure when no rig is set. Needs Read/Write.")]
        private Mesh sourceMesh;
        [SerializeField, Tooltip("Rigged model (Humanoid, Read/Write). Instantiated under this object; its skinned mesh becomes the points.")]
        private GameObject rigModel;
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
        [SerializeField, Range(0f, 0.5f), Tooltip("Points within this distance of the boss eye on screen are dimmed (screen heights, 0 = off). Look only.")]
        private float bossDimRadius = 0.16f;
        [SerializeField, Range(0.001f, 0.3f), Tooltip("Width of the soft edge inside the radius (screen heights).")]
        private float bossDimEdge = 0.06f;
        [SerializeField, Range(0f, 1f), Tooltip("Brightness of points over the boss eye. The hit flash stays full.")]
        private float bossDimBrightness = 0.25f;

        private BossCombatController _combat;
        private Transform _boss;
        private MeshRenderer _meshRenderer;
        private Renderer _renderer;
        private Material _material;
        private Mesh _pointMesh;
        private Bounds _bounds;
        private float _scale = 1f;
        private float _anchorY;
        private float _flash;
        private float _yaw;

        private GameObject _rig;
        private AvatarSkeleton _skeleton;
        private readonly AvatarPoseSolver _solver = new AvatarPoseSolver();
        private IPoseSource _pose;
        private bool _posed;

        public bool HasPoints => _pointMesh != null;

        /// <summary>True when the points are skinned to <see cref="rigModel"/> (pose capable).</summary>
        public bool IsSkinned => _skeleton != null;

        /// <summary>Pose state, for checks and Dev overlays.</summary>
        public AvatarPoseSolver Pose => _solver;

        /// <summary>The rig's bones, or null for the static figure.</summary>
        public AvatarSkeleton Skeleton => _skeleton;

        /// <summary>World width of the torso at the player plane.</summary>
        public float TorsoWorldWidth { get; private set; }

        private void Awake()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            _material = _meshRenderer.material; // per-instance copy, destroyed in OnDestroy
            _meshRenderer.enabled = false;
            Rebuild();
            UpdateScale(); // Bind may have run first
            ApplyBossDim();
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_pointMesh != null) Destroy(_pointMesh);
        }

        public void Bind(BossCombatController combat, float hitRadius)
        {
            _combat = combat;
            var look = combat.GetComponentInChildren<BossLookAt>(true);
            _boss = look != null ? look.transform : null; // same boss point as the presenters
            ApplyBossDim();
            float screenHeight = WallSpace.ScreenHeightAt(combat.OverlayCamera, combat.PlayerPlaneDistance);
            TorsoWorldWidth = hitRadius * 2f * screenHeight;
            UpdateScale();
        }

        /// <summary>
        /// Joint source for the limbs (R3 <c>UdpPoseReceiver</c>, shared with the mini character). Null keeps the
        /// rest pose (position only, as before R4). Call after Awake, in any order with <see cref="Bind"/>.
        /// No effect on the static figure.
        /// </summary>
        public void SetPoseSource(IPoseSource poseOrNull)
        {
            _pose = poseOrNull;
            _solver.Reset();
            if (_skeleton != null) _skeleton.ApplyRest();
            _posed = false;
        }

        /// <summary>Swaps the static model (drops the rig). Allocates; call at setup only.</summary>
        public void SetMesh(Mesh mesh, float torsoRatio)
        {
            sourceMesh = mesh;
            rigModel = null;
            torsoOverBounds = Mathf.Clamp(torsoRatio, 0.1f, 1f);
            Rebuild();
            UpdateScale();
        }

        public void SetVisible(bool visible)
        {
            if (_renderer == null) return;
            bool show = visible && _pointMesh != null;
            if (show && !_renderer.enabled) _solver.Reset(); // reappear in place, not flying in from the last pose
            _renderer.enabled = show;
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
            // Back to the camera, turned toward the boss. The pose below is relative to this turn.
            Quaternion rotation = Quaternion.LookRotation(cam.forward, cam.up) * Quaternion.Euler(0f, _yaw, 0f);
            Vector3 anchor = WallSpace.ToWorld(_combat.OverlayCamera, _combat.PlayerPosition,
                _combat.PlayerPlaneDistance);
            transform.SetPositionAndRotation(anchor - rotation * new Vector3(0f, _anchorY * _scale, 0f), rotation);

            UpdatePose(frameDelta);
            UpdateBossDim();

            if (_flash > 0f)
            {
                _flash = Mathf.MoveTowards(_flash, 0f, frameDelta / flashFadeSec);
                _material.SetFloat(FlashId, _flash);
            }
        }

        private void UpdatePose(float deltaTime)
        {
            if (_skeleton == null || _pose == null) return;

            float now = Time.unscaledTime;
            float age = _pose.LastPoseTime < 0f ? float.PositiveInfinity : now - _pose.LastPoseTime;
            _solver.Update(_pose.Latest, age, now, deltaTime);

            bool any = _solver.TorsoWeight > 0f;
            for (int i = 0; i < AvatarPoseSolver.LimbCount && !any; i++) any = _solver.Weight((AvatarLimb)i) > 0f;
            if (any) _skeleton.Apply(_solver);
            else if (_posed) _skeleton.ApplyRest();
            _posed = any;
        }

        private void ApplyBossDim()
        {
            if (_material == null) return;
            _material.SetFloat(BossFadeRadiusId, _boss != null ? bossDimRadius : 0f);
            _material.SetFloat(BossFadeSoftId, bossDimEdge);
            _material.SetFloat(BossFadeMinId, bossDimBrightness);
        }

        /// <summary>The boss moves on screen (pan, tilt, camera), so its eye is projected every frame.</summary>
        private void UpdateBossDim()
        {
            if (_boss == null || bossDimRadius <= 0f) return;
            Vector3 viewport = _combat.OverlayCamera.WorldToViewportPoint(_boss.position);
            _material.SetFloat(BossFadeRadiusId, viewport.z > 0f ? bossDimRadius : 0f);
            _material.SetVector(BossViewportId, new Vector4(viewport.x, viewport.y, 0f, 0f));
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
            bool wasVisible = _renderer != null && _renderer.enabled;
            if (_pointMesh != null) Destroy(_pointMesh);
            _pointMesh = null;
            if (_rig != null) Destroy(_rig);
            _rig = null;
            _skeleton = null;
            _renderer = _meshRenderer;
            GetComponent<MeshFilter>().sharedMesh = null;

            if (rigModel != null) BuildSkinned();
            else BuildStatic();

            if (_pointMesh == null) return;
            if (_material != null) _material.SetFloat(BackCenterXId, _bounds.center.x);
            _renderer.enabled = wasVisible;
        }

        private void BuildStatic()
        {
            if (sourceMesh == null) return;
            if (!sourceMesh.isReadable)
            {
                Debug.LogError($"[Presentation] Avatar mesh {sourceMesh.name} needs Read/Write enabled.", this);
                return;
            }

            _pointMesh = BuildPointMesh(sourceMesh, pointCount, Quaternion.Euler(meshEuler));
            _bounds = _pointMesh.bounds;
            Bounds culling = _bounds;
            culling.Expand(_bounds.size.magnitude * 0.05f); // quads grow in the vertex shader
            _pointMesh.bounds = culling;
            GetComponent<MeshFilter>().sharedMesh = _pointMesh;
        }

        private void BuildSkinned()
        {
            _rig = Instantiate(rigModel, transform, false);
            _rig.name = rigModel.name + " (points)";
            SetLayer(_rig.transform, gameObject.layer);

            var animator = _rig.GetComponent<Animator>();
            SkinnedMeshRenderer skin = _rig.GetComponentInChildren<SkinnedMeshRenderer>();
            if (animator == null || !animator.isHuman || skin == null || skin.sharedMesh == null)
            {
                Debug.LogError($"[Presentation] Avatar rig {rigModel.name} needs a Humanoid Animator and a skinned mesh.", this);
                Destroy(_rig);
                _rig = null;
                BuildStatic();
                return;
            }
            if (!skin.sharedMesh.isReadable)
            {
                Debug.LogError($"[Presentation] Avatar rig mesh {skin.sharedMesh.name} needs Read/Write enabled.", this);
                Destroy(_rig);
                _rig = null;
                BuildStatic();
                return;
            }

            Quaternion meshToRoot = Quaternion.Euler(meshEuler);
            _pointMesh = AvatarPointMesh.Build(skin.sharedMesh, pointCount, meshToRoot, skinned: true);
            _bounds = RestBounds(_pointMesh);
            _skeleton = new AvatarSkeleton(_rig.transform, animator, skin, meshToRoot); // caches bones, then rest pose
            animator.enabled = false; // bones are written by AvatarSkeleton only

            skin.sharedMesh = _pointMesh;
            skin.sharedMaterial = _material;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skin.receiveShadows = false;
            skin.updateWhenOffscreen = false;
            // Root bone (hips) space; the figure is 1 unit tall, so a 2 unit cube holds any pose.
            skin.localBounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            skin.enabled = false;
            _renderer = skin;
        }

        /// <summary>Bounds of the rest figure (uv1), which equal the static figure's.</summary>
        private static Bounds RestBounds(Mesh mesh)
        {
            var rest = new System.Collections.Generic.List<Vector3>(mesh.vertexCount);
            mesh.GetUVs(1, rest);
            var bounds = new Bounds(rest[0], Vector3.zero);
            for (int i = 1; i < rest.Count; i++) bounds.Encapsulate(rest[i]);
            return bounds;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>Area-weighted surface samples, so density does not depend on how the model was triangulated.</summary>
        /// <param name="rotation">Applied to every sample. The returned mesh's bounds fit the samples tightly.</param>
        public static Mesh BuildPointMesh(Mesh source, int points, Quaternion rotation) =>
            AvatarPointMesh.Build(source, points, rotation, skinned: false);
    }
}
