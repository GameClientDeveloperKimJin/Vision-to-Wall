using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VisionToWall.Network;

namespace VisionToWall.UI.Dev.Editor
{
    /// <summary>
    /// Rebuilds MiniCharacter.prefab and the MiniCharacterDev scene from code so the layout is reproducible.
    /// Menu: VisionToWall/UI/Build MiniCharacter Prefab, VisionToWall/UI/Build MiniCharacterDev Scene.
    /// Rebuilding the prefab keeps its GUID, but scene overrides on rebuilt children may be lost.
    /// </summary>
    public static class MiniCharacterBuilder
    {
        public const string PrefabPath = "Assets/03_Prefabs/UI/MiniCharacter.prefab";
        public const string DevScenePath = "Assets/02_Scripts/UI/Dev/MiniCharacter/MiniCharacterDev.unity";
        public const string BaseArtPath = "Assets/09_UI/ComboBase.png";

        /// <summary>Upper right, at 1920x1080 reference.</summary>
        public static readonly Vector2 PanelOffset = new Vector2(-36f, -36f);
        public static readonly Vector2 PanelSize = new Vector2(240f, 300f);
        private static readonly Vector2 BaseSize = new Vector2(230f, 50f);
        private static readonly Color BaseTint = new Color(1f, 1f, 1f, 0.85f);

        [MenuItem("VisionToWall/UI/Build MiniCharacter Prefab")]
        public static void BuildPrefab()
        {
            GameObject root = BuildHierarchy();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            Debug.Log(ok ? $"[MiniCharacter Builder] Saved {PrefabPath}" : $"[MiniCharacter Builder] Failed to save {PrefabPath}");
        }

        [MenuItem("VisionToWall/UI/Build MiniCharacterDev Scene")]
        public static void BuildDevScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                BuildPrefab();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null) return;
            }

            if (SceneManager.GetSceneByPath(DevScenePath).isLoaded)
            {
                Debug.LogError($"[MiniCharacter Builder] Close {DevScenePath} before rebuilding it.");
                return;
            }

            // Additive so the user's open scene is never replaced or prompted.
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.02f, 0.05f, 1f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            var network = new GameObject("NetworkPose");
            var receiver = network.AddComponent<UdpPoseReceiver>();

            var mocks = new GameObject("Mocks");
            var phase = mocks.AddComponent<MockGameSources>();

            var miniGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            var boot = new GameObject("MiniCharacterDevBootstrap").AddComponent<MiniCharacterDevBootstrap>();
            SetRef(boot, "receiver", receiver);
            SetRef(boot, "view", miniGo.GetComponent<MiniCharacterView>());
            SetRef(boot, "gameSources", phase);

            bool saved = EditorSceneManager.SaveScene(scene, DevScenePath);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
            if (saved) Debug.Log($"[MiniCharacter Builder] Saved {DevScenePath}");
            else Debug.LogError($"[MiniCharacter Builder] Failed to save {DevScenePath}");
        }

        private static GameObject BuildHierarchy()
        {
            var root = new GameObject("MiniCharacter", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(CanvasGroup), typeof(MiniCharacterView));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // under GameHud (50) and the F1 panel (100)
            canvas.enabled = false;   // the view enables it when the alpha rises
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(root.transform, false);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = PanelOffset;
            panel.sizeDelta = PanelSize;

            var baseRt = new GameObject("Base", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            baseRt.SetParent(panel, false);
            baseRt.anchorMin = baseRt.anchorMax = new Vector2(0.5f, 0f);
            baseRt.pivot = new Vector2(0.5f, 0f);
            baseRt.anchoredPosition = Vector2.zero;
            baseRt.sizeDelta = BaseSize;
            var baseImg = baseRt.GetComponent<Image>();
            baseImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BaseArtPath);
            baseImg.color = BaseTint;
            baseImg.raycastTarget = false;
            if (baseImg.sprite == null) Debug.LogWarning($"[MiniCharacter Builder] Base sprite missing: {BaseArtPath}");

            var figureRt = new GameObject("Figure", typeof(RectTransform), typeof(CanvasRenderer), typeof(NeonFigureGraphic))
                .GetComponent<RectTransform>();
            figureRt.SetParent(panel, false);
            figureRt.anchorMin = Vector2.zero;
            figureRt.anchorMax = Vector2.one;
            figureRt.offsetMin = figureRt.offsetMax = Vector2.zero;
            var figure = figureRt.GetComponent<NeonFigureGraphic>();
            figure.raycastTarget = false;

            var view = root.GetComponent<MiniCharacterView>();
            SetRef(view, "canvas", canvas);
            SetRef(view, "group", group);
            SetRef(view, "figure", figure);
            return root;
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"[MiniCharacter Builder] {target.GetType().Name}.{field} not found");
                return;
            }

            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
