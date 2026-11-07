using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VisionToWall.Core;

namespace VisionToWall.PoseTest.Editor
{
    /// <summary>
    /// Renders every <see cref="PoseTestClips"/> clip as a 640x480 front view PNG sequence plus the truth CSV:
    /// <c>Temp/pose_check/&lt;clip&gt;/frame_00000.png</c>, <c>truth.csv</c>. Runs in a preview scene (nothing in the
    /// open scenes changes) and spreads the frames over editor updates.
    /// <para>
    /// Setup (R3 installation rule: chest and both elbows in view; here the whole body is in view): avatar 1.7 m
    /// tall at 3 m facing the camera, level camera at 1.05 m, vertical FOV 43° (about a Logi C270), solid background,
    /// key + fill directional lights, untextured grey URP Lit (no shadows).
    /// </para>
    /// <para>
    /// Look (2026-11-08 measurement, correct 1.7 m scale): the FBX's white material on blue-grey (0.32, 0.36, 0.42),
    /// grey 0.55 on near-black, skin tone on near-black, and a camera at 1.4 m / 3.5 m all gave 100 % detection and
    /// the same angle and depth errors. Default: grey 0.55 on (0.12, 0.12, 0.13). (An earlier 0 % idle result came
    /// from a render with the avatar scaled twice, head out of frame — not from the material.)
    /// </para>
    /// </summary>
    public static class PoseTestRenderer
    {
        public const string OutputRoot = "Temp/pose_check";
        public const string ModelPath = "Assets/07_Models/Avatar/PlayerAvatar_Rig.fbx";
        public const int Width = 640;
        public const int Height = 480;
        public const float AvatarHeightM = 1.7f;
        public const float AvatarDistanceM = 3f;
        public const float CameraHeightM = 1.05f;
        public const float VerticalFovDeg = 43f;
        private const int FramesPerUpdate = 6;

        /// <summary>Render look. Changed only to compare detection (see spec 56 작업 로그).</summary>
        public sealed class Look
        {
            public string Name = "grey_darkbg";
            public Color Background = new Color(0.12f, 0.12f, 0.13f);
            /// <summary>null = the FBX's own material (white URP Lit); else a URP Lit of this base color.</summary>
            public Color? ModelColor = new Color(0.55f, 0.55f, 0.55f);
            public string OutputRoot = PoseTestRenderer.OutputRoot;
            public float CameraHeight = CameraHeightM;
            public float Distance = AvatarDistanceM;
        }

        public static Look Current = new Look();

        private static Job _job;

        [MenuItem("VisionToWall/PoseTest/1. Render Clips (PNG + truth CSV)")]
        public static void RenderAllMenu() => Render(null);

        public static bool IsRunning => _job != null;

        /// <summary>Starts rendering <paramref name="clipNames"/> (null = all). Progress and the end go to the console.</summary>
        public static void Render(string[] clipNames)
        {
            if (_job != null)
            {
                Debug.LogWarning("[PoseTest] render already running");
                return;
            }

            var clips = new List<PoseClip>();
            foreach (PoseClip c in PoseTestClips.All)
                if (clipNames == null || Array.IndexOf(clipNames, c.Name) >= 0) clips.Add(c);
            _job = new Job(clips);
            EditorApplication.update += Step;
        }

        private static void Step()
        {
            try
            {
                if (_job.Step(FramesPerUpdate)) return;
                Finish(null);
            }
            catch (Exception e)
            {
                Finish(e);
            }
        }

        private static void Finish(Exception e)
        {
            EditorApplication.update -= Step;
            string summary = _job.Summary;
            _job.Dispose();
            _job = null;
            if (e != null) Debug.LogError("[PoseTest] render failed: " + e);
            else Debug.Log("[PoseTest] render done: " + summary);
        }

        /// <summary>Creates the render scene. Caller disposes it with <see cref="Stage.Dispose"/>.</summary>
        public static Stage CreateStage() => new Stage();

        public sealed class Stage : IDisposable
        {
            public readonly Scene Scene;
            public readonly Camera Camera;
            public readonly PoseRig Rig;
            public readonly Vector3 BasePosition;
            private readonly RenderTexture _rt;
            private readonly Texture2D _readback;
            private readonly SkinnedMeshRenderer _skin;
            private readonly Mesh _bakedMesh;
            private readonly Material _material;

            public Stage() : this(Current)
            {
            }

            public Stage(Look look)
            {
                BasePosition = new Vector3(0f, 0f, look.Distance);
                Scene = EditorSceneManager.NewPreviewScene();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null) throw new InvalidOperationException("Model not found: " + ModelPath);

                GameObject avatar = UnityEngine.Object.Instantiate(model);
                avatar.name = "PoseTestAvatar";
                SceneManager.MoveGameObjectToScene(avatar, Scene);
                avatar.transform.SetPositionAndRotation(BasePosition, Quaternion.Euler(0f, 180f, 0f));
                var animator = avatar.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                    throw new InvalidOperationException("PlayerAvatar_Rig must be imported as Humanoid.");

                // Scale to the target height from the bind pose (head top to foot sole).
                _skin = avatar.GetComponentInChildren<SkinnedMeshRenderer>();
                // SkinnedMeshRenderer.bounds is not reliable before the first skinning and the shared mesh is Z-up
                // (FBX axes), so measure a baked bind pose.
                var probe = new Mesh();
                _skin.BakeMesh(probe, true);
                float bindHeight = probe.bounds.size.y * _skin.transform.lossyScale.y;
                UnityEngine.Object.DestroyImmediate(probe);
                avatar.transform.localScale = Vector3.one * (AvatarHeightM / bindHeight);

                // Edit-mode Camera.Render does not re-skin a SkinnedMeshRenderer whose bones changed inside the same
                // editor update, so every frame is baked into a plain mesh drawn by a sibling MeshRenderer.
                _skin.forceRenderingOff = true;
                var baked = new GameObject("PoseTestBaked");
                baked.transform.SetParent(_skin.transform.parent, false);
                baked.transform.localPosition = _skin.transform.localPosition;
                baked.transform.localRotation = _skin.transform.localRotation;
                baked.transform.localScale = _skin.transform.localScale;
                _bakedMesh = new Mesh { name = "PoseTestBaked" };
                baked.AddComponent<MeshFilter>().sharedMesh = _bakedMesh;
                var bakedRenderer = baked.AddComponent<MeshRenderer>();
                bakedRenderer.sharedMaterials = _skin.sharedMaterials;
                if (look.ModelColor.HasValue)
                {
                    _material = new Material(_skin.sharedMaterial) { name = "PoseTestModel" };
                    _material.SetColor("_BaseColor", look.ModelColor.Value);
                    _material.SetFloat("_Smoothness", 0.3f);
                    _material.SetTexture("_BaseMap", null);
                    bakedRenderer.sharedMaterial = _material;
                }

                var camGo = new GameObject("PoseTestCamera");
                SceneManager.MoveGameObjectToScene(camGo, Scene);
                camGo.transform.SetPositionAndRotation(new Vector3(0f, look.CameraHeight, 0f), Quaternion.identity);
                Camera = camGo.AddComponent<Camera>();
                Camera.scene = Scene;
                Camera.fieldOfView = VerticalFovDeg;
                Camera.aspect = Width / (float)Height;
                Camera.nearClipPlane = 0.1f;
                Camera.farClipPlane = 20f;
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = look.Background;

                AddLight("Key", Quaternion.Euler(30f, 20f, 0f), 1.2f, Color.white);
                AddLight("Fill", Quaternion.Euler(10f, -40f, 0f), 0.45f, new Color(0.85f, 0.9f, 1f));

                _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                Camera.targetTexture = _rt;
                _readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);

                Rig = new PoseRig(animator);
                Rig.CalibrateFloor(BasePosition);

                // Guard against the scale being applied twice (BakeMesh flag) and warm up: the very first
                // Camera.Render of a new stage comes out without the model (measured: frame 0 was background only).
                Rig.ResetToBind(BasePosition);
                _skin.BakeMesh(_bakedMesh, true);
                float drawn = _bakedMesh.bounds.size.y * baked.transform.lossyScale.y;
                if (Mathf.Abs(drawn - AvatarHeightM) > 0.05f * AvatarHeightM)
                    throw new InvalidOperationException($"Drawn avatar is {drawn:F2} m tall, expected {AvatarHeightM} m.");
                RenderPng();
                RenderPng();
            }

            /// <summary>Drawn (baked) avatar height in meters, from the last bake.</summary>
            public float DrawnHeight => _bakedMesh.bounds.size.y * _skin.transform.lossyScale.y;

            private void AddLight(string name, Quaternion rot, float intensity, Color color)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.rotation = rot;
                var l = go.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = intensity;
                l.color = color;
                l.shadows = LightShadows.None;
            }

            public byte[] RenderPng()
            {
                _skin.BakeMesh(_bakedMesh, true); // unscaled: the sibling transform applies the scale
                _bakedMesh.RecalculateBounds();
                Camera.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                // No Apply(): EncodeToPNG reads the CPU copy. Apply() per frame piled up D3D12 upload memory
                // (measured +1.25 MB per call, never released inside the editor update) and crashed the editor twice.
                RenderTexture.active = prev;
                return _readback.EncodeToPNG();
            }

            public void Dispose()
            {
                if (Camera != null) Camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(_rt);
                UnityEngine.Object.DestroyImmediate(_readback);
                UnityEngine.Object.DestroyImmediate(_bakedMesh);
                if (_material != null) UnityEngine.Object.DestroyImmediate(_material);
                EditorSceneManager.ClosePreviewScene(Scene);
            }
        }

        /// <summary>Frame time in ms, identical to <c>pose_from_media.py</c> (floor of i * 1000 / fps).</summary>
        public static int FrameMs(int i, float fps) => (int)Math.Floor(i * 1000.0 / fps);

        private sealed class Job : IDisposable
        {
            private readonly List<PoseClip> _clips;
            private readonly Stage _stage;
            private readonly float[] _joints = new float[PoseProtocol.JointCount * PoseProtocol.FloatsPerJoint];
            private readonly Quaternion[] _bones = new Quaternion[PoseRig.TruthBones.Length];
            private int _clip = -1;
            private int _frame;
            private int _frames;
            private string _dir;
            private StringBuilder _csv;
            private int _total;
            private readonly DateTime _start = DateTime.Now;

            public Job(List<PoseClip> clips)
            {
                _clips = clips;
                _stage = new Stage();
                NextClip();
            }

            public string Summary =>
                $"{_clips.Count} clips, {_total} frames in {(DateTime.Now - _start).TotalSeconds:F0}s → {Current.OutputRoot}/<clip>/";

            private void NextClip()
            {
                _clip++;
                if (_clip >= _clips.Count) return;
                PoseClip c = _clips[_clip];
                _dir = Path.Combine(Current.OutputRoot, c.Name);
                if (Directory.Exists(_dir))
                    foreach (string f in Directory.GetFiles(_dir, "frame_*.png")) File.Delete(f);
                Directory.CreateDirectory(_dir);
                _frame = 0;
                _frames = c.FrameCount(PoseTestClips.Fps);
                _csv = new StringBuilder(_frames * 1200);
                _csv.Append(PoseTruth.Header()).Append('\n');
            }

            /// <summary>Returns true while there is more to render.</summary>
            public bool Step(int budget)
            {
                for (int n = 0; n < budget && _clip < _clips.Count; n++)
                {
                    PoseClip c = _clips[_clip];
                    float t = _frame / PoseTestClips.Fps;
                    _stage.Rig.Apply(c.Evaluate(t), _stage.BasePosition);
                    byte[] png = _stage.RenderPng();
                    File.WriteAllBytes(Path.Combine(_dir, $"frame_{_frame:D5}.png"), png);
                    int count = _stage.Rig.SampleJoints(_stage.Camera, _joints);
                    _stage.Rig.SampleBoneDeltas(_bones);
                    PoseTruth.AppendRow(_csv, FrameMs(_frame, PoseTestClips.Fps), count, _joints,
                        _stage.Rig.Bone(HumanBodyBones.Hips).position, _bones);
                    _frame++;
                    _total++;
                    if (_frame >= _frames)
                    {
                        File.WriteAllText(Path.Combine(_dir, "truth.csv"), _csv.ToString());
                        Debug.Log($"[PoseTest] {c.Name}: {_frames} frames");
                        NextClip();
                    }
                }

                return _clip < _clips.Count;
            }

            public void Dispose() => _stage.Dispose();
        }
    }
}
