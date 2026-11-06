using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace VisionToWall.UI.Editor
{
    /// <summary>
    /// Rebuilds GameHud.prefab from code so the layout is reproducible. Layout follows the art reference at 1920x1080.
    /// Menu: VisionToWall/UI/Build GameHud Prefab
    /// </summary>
    public static class GameHudBuilder
    {
        public const string PrefabPath = "Assets/03_Prefabs/UI/GameHud.prefab";

        public const string BaseArtPath = "Assets/09_UI/ComboBase.png";

        /// <summary>A VARCO frame (35 AR-03). The hole is the transparent inside, in sprite pixels from each edge.</summary>
        private readonly struct FrameArt
        {
            public readonly string Path;
            public readonly int Left, Top, Right, Bottom;
            public readonly int SideBorder;
            public readonly float Multiplier;

            public FrameArt(string path, int left, int top, int right, int bottom, int sideBorder, float multiplier)
            {
                Path = path;
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
                SideBorder = sideBorder;
                Multiplier = multiplier;
            }
        }

        // Measured from the alpha channel. Side border covers the corner decoration; the middle stretches.
        private static readonly FrameArt BossFrame = new FrameArt("Assets/09_UI/BossHpFrame.png", 47, 28, 48, 31, 67, 3f);
        private static readonly FrameArt PlayerFrame = new FrameArt("Assets/09_UI/PlayerHpFrame.png", 53, 35, 54, 35, 73, 3f);
        private static readonly FrameArt ComboFrame = new FrameArt("Assets/09_UI/ComboPanelFrame.png", 72, 46, 72, 33, 110, 2.4f);
        private static readonly FrameArt BannerFrame = new FrameArt("Assets/09_UI/ResultBannerFrame.png", 45, 26, 45, 28, 65, 0.8f);

        /// <summary>Keeps the rectangular fill clear of the frame's cut corners.</summary>
        private const float BarFrameMargin = 3f;

        private static readonly Color Plate = new Color(0.01f, 0.03f, 0.07f, 0.55f);
        private static readonly Color Track = new Color(0.02f, 0.04f, 0.08f, 1f);
        private static readonly Color TextColor = new Color(0.95f, 0.97f, 1f, 1f);
        private static readonly Color BossRed = new Color(1f, 0.25f, 0.24f, 1f);
        private static readonly Color BossEdge = new Color(1f, 0.55f, 0.52f, 0.9f);
        private static readonly Color PlayerCyan = new Color(0.25f, 0.9f, 1f, 1f);
        private static readonly Color PlayerEdge = new Color(0.6f, 0.95f, 1f, 0.9f);
        private static readonly Color Dim = new Color(0.75f, 0.85f, 0.92f, 1f);

        private static TMP_FontAsset _font;

        [MenuItem("VisionToWall/UI/Build GameHud Prefab")]
        public static void BuildPrefab()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EngineerPanelBuilder.FontPath);
            if (_font == null)
            {
                Debug.LogError($"[UI Builder] Font not found: {EngineerPanelBuilder.FontPath}");
                return;
            }

            ImportFrame(BossFrame);
            ImportFrame(PlayerFrame);
            ImportFrame(ComboFrame);
            ImportFrame(BannerFrame);
            ImportSprite(BaseArtPath, Vector4.zero);

            GameObject root = BuildHierarchy();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            Debug.Log(ok ? $"[UI Builder] Saved {PrefabPath}" : $"[UI Builder] Failed to save {PrefabPath}");
        }

        private static void ImportFrame(FrameArt art)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(art.Path);
            if (texture == null) return;

            // Stops short of the middle so one row and column can stretch.
            int vertical = texture.height / 2 - 2;
            ImportSprite(art.Path, new Vector4(art.SideBorder, vertical, art.SideBorder, vertical));
        }

        private static void ImportSprite(string path, Vector4 border)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        private static GameObject BuildHierarchy()
        {
            var root = new GameObject("GameHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(CanvasGroup), typeof(GameHudView));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // under the F1 panel (100)
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var view = root.GetComponent<GameHudView>();
            SetRef(view, "canvas", canvas);
            SetRef(view, "canvasGroup", group);

            BuildBoss(root.transform, view);
            BuildPlayer(root.transform, view);
            BuildCombo(root.transform, view);
            BuildBanner(root.transform, view);

            SetLayerRecursive(root.transform, LayerMask.NameToLayer("UI"));
            return root;
        }

        // Top center: title, red bar with percent on its right, time underneath.
        private static void BuildBoss(Transform parent, GameHudView view)
        {
            var box = Box("Boss", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(960f, 114f));
            box.gameObject.AddComponent<Image>().color = Plate;
            box.GetComponent<Image>().raycastTarget = false;

            Label("Title", box, "BOSS: EYE OF THE FUTURE", 36, TextColor, TextAlignmentOptions.Center,
                new Vector2(0f, -2f), new Vector2(940f, 42f), new Vector2(0.5f, 1f));

            RectTransform fill = Bar("BossBar", box, new Vector2(-60f, -47f), new Vector2(720f, 20f), BossRed, BossEdge, BossFrame);
            SetRef(view, "bossFill", fill);

            var percent = Label("Percent", box, "100%", 30, TextColor, TextAlignmentOptions.MidlineLeft,
                new Vector2(380f, -38f), new Vector2(120f, 38f), new Vector2(0.5f, 1f));
            SetRef(view, "bossPercent", percent);

            var time = Label("RemainingTime", box, "01:00", 26, Dim, TextAlignmentOptions.Center,
                new Vector2(0f, -76f), new Vector2(240f, 34f), new Vector2(0.5f, 1f));
            SetRef(view, "remainingTime", time);
        }

        // Lower middle, right of center: label, cyan bar, shield status underneath.
        private static void BuildPlayer(Transform parent, GameHudView view)
        {
            var box = Box("Player", parent, new Vector2(0.6f, 0f), new Vector2(0f, 0f), new Vector2(0f, 30f), new Vector2(460f, 134f));
            box.gameObject.AddComponent<Image>().color = Plate;
            box.GetComponent<Image>().raycastTarget = false;

            var label = Label("Label", box, "PLAYER HP: 100%", 32, TextColor, TextAlignmentOptions.MidlineLeft,
                new Vector2(16f, -8f), new Vector2(430f, 42f), new Vector2(0f, 1f));
            SetRef(view, "playerLabel", label);

            RectTransform fill = Bar("PlayerBar", box, new Vector2(230f, -58f), new Vector2(404f, 20f), PlayerCyan, PlayerEdge,
                PlayerFrame, new Vector2(0f, 1f));
            SetRef(view, "playerFill", fill);

            var shield = Label("Shield", box, "SHIELD ACTIVE (실드 가동!)", 28, PlayerCyan, TextAlignmentOptions.MidlineLeft,
                new Vector2(16f, -94f), new Vector2(430f, 38f), new Vector2(0f, 1f));
            shield.gameObject.SetActive(false);
            SetRef(view, "shieldStatus", shield.gameObject);
        }

        // Lower right: hologram panel on an elliptical base.
        private static void BuildCombo(Transform parent, GameHudView view)
        {
            var box = Box("Combo", parent, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 30f), new Vector2(220f, 210f));

            var baseRing = Img("Base", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(210f, 45f), Color.white);
            baseRing.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BaseArtPath);

            // Parent of the frame, so its size is the frame's hole.
            var panel = Img("Panel", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f),
                new Vector2(150f, 150f), new Color(0.02f, 0.1f, 0.18f, 0.55f));
            var panelRt = (RectTransform)panel.transform;
            if (Frame(panelRt, ComboFrame, 0f) == null) Edge(panel, PlayerEdge);

            Label("Title", panelRt, "COMBO", 30, PlayerEdge, TextAlignmentOptions.Center,
                new Vector2(0f, -6f), new Vector2(150f, 38f), new Vector2(0.5f, 1f));
            var value = Label("Value", panelRt, "x2", 80, TextColor, TextAlignmentOptions.Center,
                new Vector2(0f, -46f), new Vector2(150f, 96f), new Vector2(0.5f, 1f));

            box.gameObject.SetActive(false);
            SetRef(view, "comboRoot", box.gameObject);
            SetRef(view, "comboValue", value);
        }

        private static void BuildBanner(Transform parent, GameHudView view)
        {
            var box = Box("ResultBanner", parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            box.anchorMax = new Vector2(1f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(0f, 230f);
            box.anchoredPosition = Vector2.zero;

            var group = box.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var plate = Img("Plate", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 180f),
                new Color(0f, 0f, 0f, 0.55f));
            Image frame = Frame((RectTransform)plate.transform, BannerFrame, 0f);

            var label = Label("Label", box, ResultBannerView.VictoryText, 150, TextColor, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(1200f, 180f), new Vector2(0.5f, 0.5f));

            var banner = box.gameObject.AddComponent<ResultBannerView>();
            SetRef(banner, "canvasGroup", group);
            SetRef(banner, "label", label);
            SetRef(banner, "frame", frame);
            SetRef(view, "resultBanner", banner);
        }

        /// <summary>Returns the fill. Its width is driven through anchorMax.x, so no sprite is needed.</summary>
        private static RectTransform Bar(string name, RectTransform parent, Vector2 topCenter, Vector2 size, Color fill, Color edge,
            FrameArt art, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0.5f, 1f);
            var track = Img(name, parent, a, new Vector2(0.5f, 1f), topCenter, size, Track);

            var fillImg = Img("Fill", (RectTransform)track.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, fill);
            var fillRt = (RectTransform)fillImg.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            if (Frame((RectTransform)track.transform, art, BarFrameMargin) == null) Edge(track, edge);
            return fillRt;
        }

        /// <summary>
        /// Placed so its hole surrounds the parent by <paramref name="margin"/>, drawn after the parent's children
        /// made so far. Null when the art is not imported.
        /// </summary>
        private static Image Frame(RectTransform parent, FrameArt art, float margin)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(art.Path);
            if (sprite == null) return null;

            var frame = Img("Frame", parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);
            frame.sprite = sprite;
            frame.type = Image.Type.Sliced;
            frame.pixelsPerUnitMultiplier = art.Multiplier;

            var rt = (RectTransform)frame.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-(art.Left / art.Multiplier + margin), -(art.Bottom / art.Multiplier + margin));
            rt.offsetMax = new Vector2(art.Right / art.Multiplier + margin, art.Top / art.Multiplier + margin);
            return frame;
        }

        /// <summary>Outline, because a border image under a see-through fill would wash the fill out.</summary>
        private static void Edge(Image image, Color color)
        {
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;
        }

        private static RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        private static Image Img(string name, RectTransform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color color)
        {
            var img = Box(name, parent, anchor, pivot, position, size).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Label(string name, RectTransform parent, string text, float size, Color color,
            TextAlignmentOptions alignment, Vector2 position, Vector2 box, Vector2 anchor)
        {
            var rt = Box(name, parent, anchor, anchor, position, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.fontSize = size;
            t.color = color;
            t.alignment = alignment;
            t.text = text;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
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
    }
}
