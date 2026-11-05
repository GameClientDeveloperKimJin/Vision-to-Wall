using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VisionToWall.UI.Dev;

namespace VisionToWall.UI.Editor
{
    /// <summary>
    /// Rebuilds EngineerPanel.prefab and the UIDev scene from code so the layout is reproducible.
    /// Menu: VisionToWall/UI/...
    /// </summary>
    public static class EngineerPanelBuilder
    {
        public const string PrefabPath = "Assets/03_Prefabs/UI/EngineerPanel.prefab";
        public const string DevScenePath = "Assets/02_Scripts/UI/Dev/UIDev.unity";
        public const string FontPath = "Assets/06_Font/GmarketSansTTFMedium SDF.asset";

        private const float PanelWidth = 380f;
        private const float Pad = 12f;
        private static readonly Color PanelBg = new Color(0.05f, 0.06f, 0.08f, 0.85f);
        private static readonly Color TextColor = new Color(0.92f, 0.94f, 0.96f, 1f);
        private static readonly Color DimText = new Color(0.65f, 0.70f, 0.75f, 1f);
        private static readonly Color Accent = new Color(0.35f, 0.85f, 1f, 1f);

        private static TMP_FontAsset _font;

        [MenuItem("VisionToWall/UI/Build EngineerPanel Prefab")]
        public static void BuildPrefab()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (_font == null)
            {
                Debug.LogError($"[UI Builder] Font not found: {FontPath}");
                return;
            }

            EnsureFolder("Assets/03_Prefabs", "UI");
            GameObject root = BuildHierarchy();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            Debug.Log(ok ? $"[UI Builder] Saved {PrefabPath}" : $"[UI Builder] Failed to save {PrefabPath}");
        }

        [MenuItem("VisionToWall/UI/Build UIDev Scene")]
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
                Debug.LogError($"[UI Builder] Close {DevScenePath} before rebuilding it.");
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
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.2f, 1f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            var mocks = new GameObject("Mocks");
            var points = mocks.AddComponent<MockPointSource>();
            var stats = mocks.AddComponent<MockNetworkStats>();
            var state = mocks.AddComponent<MockExhibitionState>();
            var views = mocks.AddComponent<MockDebugViewProvider>();
            SetRef(stats, "source", points);
            SetRef(state, "source", points);
            SetRef(views, "source", points);

            var panelGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var panel = panelGo.GetComponent<EngineerPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("startOpen").boolValue = true; // scene override: open for quick checks
            so.ApplyModifiedPropertiesWithoutUndo();

            var boot = new GameObject("UIDevBootstrap").AddComponent<UIDevBootstrap>();
            SetRef(boot, "panel", panel);
            SetRef(boot, "debugViews", views);
            SetRef(boot, "points", points);
            SetRef(boot, "stats", stats);
            SetRef(boot, "state", state);

            bool saved = EditorSceneManager.SaveScene(scene, DevScenePath);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
            if (saved) Debug.Log($"[UI Builder] Saved {DevScenePath}");
            else Debug.LogError($"[UI Builder] Failed to save {DevScenePath} (close it first if it is open)");
        }

        private static GameObject BuildHierarchy()
        {
            var root = new GameObject("EngineerPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(EngineerPanel));
            root.layer = LayerMask.NameToLayer("UI");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // Panel: left edge, full height, fixed width.
            var panel = NewUI("Panel", root.transform);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 0.5f);
            prt.sizeDelta = new Vector2(PanelWidth, 0f);
            prt.anchoredPosition = Vector2.zero;
            var bg = panel.AddComponent<Image>();
            bg.color = PanelBg;
            panel.AddComponent<RectMask2D>(); // clip long log lines at the panel edge
            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var header = Text("Header", panel.transform, "Vision-to-Wall 엔지니어 패널  [F1]", 20, 28);
            header.color = Accent;
            header.fontStyle = FontStyles.Bold;

            var status = Text("Status", panel.transform, "FPS --  |  상태: --\n카메라: --", 15, 42);

            // Debug views row
            var views = NewUI("Views", panel.transform);
            var hlg = views.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            Layout(views, -1f, 108f);
            RawImage source = View(views.transform, "Source", "원본", 116f, 87f);
            RawImage diff = View(views.transform, "Diff", "차분", 87f, 87f);
            RawImage grid = View(views.transform, "Grid", "그리드", 87f, 87f);

            var network = Text("Network", panel.transform, "UDP 포트 ----\n마지막 패킷 -", 15, 42);

            var logHeader = Text("PacketLogHeader", panel.transform, "─ 패킷 로그 (최근 10개) ─────", 13, 18);
            logHeader.color = DimText;
            var logText = Text("PacketLog", panel.transform, "(수신 패킷 없음)", 11, 10 * 15f);
            var logView = logText.gameObject.AddComponent<PacketLogView>();
            SetRef(logView, "text", logText);

            // Config editor
            var configGo = NewUI("Config", panel.transform);
            var cvlg = configGo.AddComponent<VerticalLayoutGroup>();
            cvlg.spacing = 6f;
            cvlg.childControlWidth = true;
            cvlg.childControlHeight = true;
            cvlg.childForceExpandWidth = true;
            cvlg.childForceExpandHeight = false;
            var editor = configGo.AddComponent<ConfigEditorView>();

            DefaultControls.Resources res = UiResources();

            var thrRow = Row(configGo.transform, "ThresholdRow", 26f);
            Layout(Text("Label", thrRow.transform, "감도", 15, 26).gameObject, 90f, 26f);
            var sliderGo = DefaultControls.CreateSlider(res);
            sliderGo.name = "ThresholdSlider";
            sliderGo.transform.SetParent(thrRow.transform, false);
            Layout(sliderGo, 190f, 20f, flexibleWidth: 1f);
            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = ConfigEditorView.MinThreshold;
            slider.maxValue = ConfigEditorView.MaxThreshold;
            slider.value = 0.15f;
            var thrValue = Text("Value", thrRow.transform, "0.15", 15, 26);
            thrValue.alignment = TextAlignmentOptions.MidlineRight;
            Layout(thrValue.gameObject, 48f, 26f);

            var mirRow = Row(configGo.transform, "MirrorRow", 26f);
            Layout(Text("Label", mirRow.transform, "좌우 반전", 15, 26).gameObject, 90f, 26f);
            var toggleGo = DefaultControls.CreateToggle(res);
            toggleGo.name = "MirrorToggle";
            Object.DestroyImmediate(toggleGo.transform.Find("Label").gameObject); // legacy Text
            toggleGo.transform.SetParent(mirRow.transform, false);
            Layout(toggleGo, 24f, 24f);
            var toggle = toggleGo.GetComponent<Toggle>();
            toggle.isOn = true;

            // Hand sensor section (visible only when sensorMode == "external")
            var handGo = NewUI("HandSection", configGo.transform);
            var hvlg = handGo.AddComponent<VerticalLayoutGroup>();
            hvlg.spacing = 6f;
            hvlg.childControlWidth = true;
            hvlg.childControlHeight = true;
            hvlg.childForceExpandWidth = true;
            hvlg.childForceExpandHeight = false;
            var (speedSlider, speedValue) = SliderRow(handGo.transform, res, "HandSpeedRefRow", "손 속도 기준",
                ConfigEditorView.MinHandSpeedRef, ConfigEditorView.MaxHandSpeedRef, 1.5f);
            var (minSlider, minValue) = SliderRow(handGo.transform, res, "HandMinSpeedRow", "정지 판정 속도",
                ConfigEditorView.MinHandMinSpeed, ConfigEditorView.MaxHandMinSpeed, 0.05f);
            var stillRow = Row(handGo.transform, "HandEmitStillRow", 26f);
            Layout(Text("Label", stillRow.transform, "정지한 손도 표시", 15, 26).gameObject, 120f, 26f);
            var stillGo = DefaultControls.CreateToggle(res);
            stillGo.name = "HandEmitStillToggle";
            Object.DestroyImmediate(stillGo.transform.Find("Label").gameObject); // legacy Text
            stillGo.transform.SetParent(stillRow.transform, false);
            Layout(stillGo, 24f, 24f);
            var stillToggle = stillGo.GetComponent<Toggle>();
            stillToggle.isOn = false;
            handGo.SetActive(false);

            var saveRow = Row(configGo.transform, "SaveRow", 32f);
            var buttonGo = DefaultControls.CreateButton(res);
            buttonGo.name = "SaveButton";
            Object.DestroyImmediate(buttonGo.GetComponentInChildren<Text>().gameObject); // legacy Text
            buttonGo.transform.SetParent(saveRow.transform, false);
            Layout(buttonGo, 130f, 30f);
            var btnLabel = Text("Label", buttonGo.transform, "설정 저장", 15, 30);
            btnLabel.color = new Color(0.1f, 0.1f, 0.12f, 1f);
            btnLabel.alignment = TextAlignmentOptions.Center;
            Stretch((RectTransform)btnLabel.transform);
            var saveStatus = Text("SaveStatus", saveRow.transform, "", 15, 30);
            saveStatus.color = new Color(0.45f, 1f, 0.55f, 1f);
            Layout(saveStatus.gameObject, 180f, 30f, flexibleWidth: 1f);

            var notice = Text("RestartNotice", configGo.transform, ConfigEditorView.RestartNoticeText, 12, 84);
            notice.color = new Color(1f, 0.8f, 0.4f, 1f);
            notice.textWrappingMode = TextWrappingModes.Normal;

            SetRef(editor, "thresholdRow", thrRow);
            SetRef(editor, "thresholdSlider", slider);
            SetRef(editor, "handSection", handGo);
            SetRef(editor, "handSpeedRefSlider", speedSlider);
            SetRef(editor, "handSpeedRefValue", speedValue);
            SetRef(editor, "handMinSpeedSlider", minSlider);
            SetRef(editor, "handMinSpeedValue", minValue);
            SetRef(editor, "handEmitStillToggle", stillToggle);
            SetRef(editor, "thresholdValue", thrValue);
            SetRef(editor, "mirrorToggle", toggle);
            SetRef(editor, "saveButton", buttonGo.GetComponent<Button>());
            SetRef(editor, "saveStatus", saveStatus);
            SetRef(editor, "restartNotice", notice);

            var ep = root.GetComponent<EngineerPanel>();
            SetRef(ep, "panelRoot", panel);
            SetRef(ep, "statusText", status);
            SetRef(ep, "networkText", network);
            SetRef(ep, "viewsRoot", views);
            SetRef(ep, "sourceView", source);
            SetRef(ep, "diffView", diff);
            SetRef(ep, "gridView", grid);
            SetRef(ep, "packetLog", logView);
            SetRef(ep, "configEditor", editor);

            SetLayerRecursive(root.transform, LayerMask.NameToLayer("UI"));
            return root;
        }

        private static RawImage View(Transform parent, string name, string caption, float w, float h)
        {
            var col = NewUI(name, parent);
            var v = col.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = false;
            Layout(col, w, h + 18f);

            var cap = Text("Caption", col.transform, caption, 12, 16);
            cap.color = DimText;
            Layout(cap.gameObject, w, 16f);

            var imgGo = NewUI("Image", col.transform);
            var img = imgGo.AddComponent<RawImage>();
            img.color = Color.white;
            img.raycastTarget = false;
            Layout(imgGo, w, h);
            return img;
        }

        private static (Slider slider, TextMeshProUGUI value) SliderRow(Transform parent, DefaultControls.Resources res,
            string name, string label, float min, float max, float initial)
        {
            var row = Row(parent, name, 26f);
            Layout(Text("Label", row.transform, label, 15, 26).gameObject, 120f, 26f);
            var sliderGo = DefaultControls.CreateSlider(res);
            sliderGo.name = name.Replace("Row", "Slider");
            sliderGo.transform.SetParent(row.transform, false);
            Layout(sliderGo, 160f, 20f, flexibleWidth: 1f);
            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;
            var value = Text("Value", row.transform, initial.ToString("0.00"), 15, 26);
            value.alignment = TextAlignmentOptions.MidlineRight;
            Layout(value.gameObject, 48f, 26f);
            return (slider, value);
        }

        private static GameObject Row(Transform parent, string name, float height)
        {
            var row = NewUI(name, parent);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            Layout(row, -1f, height);
            return row;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string text, float size, float height)
        {
            var go = NewUI(name, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.color = TextColor;
            t.text = text;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow; // clipped by the panel RectMask2D
            t.alignment = TextAlignmentOptions.TopLeft;
            t.raycastTarget = false;
            Layout(go, -1f, height);
            return t;
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Layout(GameObject go, float width, float height, float flexibleWidth = -1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = flexibleWidth;
        }

        private static void Stretch(RectTransform rt)
        {
            Object.DestroyImmediate(rt.GetComponent<LayoutElement>());
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static DefaultControls.Resources UiResources()
        {
            return new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"[UI Builder] {target.GetType().Name}.{field} not found");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            if (layer < 0) return;
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
            Directory.CreateDirectory(parent + "/" + child);
        }
    }
}
