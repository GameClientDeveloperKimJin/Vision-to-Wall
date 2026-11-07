using UnityEngine;
using UnityEngine.UI;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Draws the neon joint figure as one UI mesh: glowing dots for joints, glowing lines for bones, a ring for the head.
    /// Two passes (wide cyan glow, thin bright core) share one procedural glow texture, so it is a single draw call.
    /// Reads a <see cref="PoseSmoother"/>; the mesh is rebuilt only when <see cref="Refresh"/> marks it dirty.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class NeonFigureGraphic : MaskableGraphic
    {
        /// <summary>Bone pairs (spec 53 3절).</summary>
        public static readonly PoseJoint[] Bones =
        {
            PoseJoint.LeftShoulder, PoseJoint.RightShoulder,
            PoseJoint.LeftShoulder, PoseJoint.LeftElbow,
            PoseJoint.LeftElbow, PoseJoint.LeftWrist,
            PoseJoint.RightShoulder, PoseJoint.RightElbow,
            PoseJoint.RightElbow, PoseJoint.RightWrist,
            PoseJoint.LeftShoulder, PoseJoint.LeftHip,
            PoseJoint.RightShoulder, PoseJoint.RightHip,
            PoseJoint.LeftHip, PoseJoint.RightHip,
            PoseJoint.LeftHip, PoseJoint.LeftKnee,
            PoseJoint.LeftKnee, PoseJoint.LeftAnkle,
            PoseJoint.RightHip, PoseJoint.RightKnee,
            PoseJoint.RightKnee, PoseJoint.RightAnkle,
        };

        public const int BoneCount = 12;
        private const int RingSegments = 20;
        private const int TextureSize = 64;

        [Header("Layout (body units: torso length = 1)")]
        [Tooltip("Body center in the rect, 0..1. The figure spans about +1.9 (raised hands) to -2.1 (ankles).")]
        [SerializeField] private Vector2 centerInRect = new Vector2(0.5f, 0.56f);
        [Tooltip("Pixels per torso length, as a fraction of the rect height.")]
        [SerializeField, Range(0.05f, 0.5f)] private float unitPerHeight = 0.22f;
        [SerializeField, Range(0.1f, 0.6f)] private float headRadius = 0.3f;

        [Header("Style (pixels at unit = 100)")]
        [SerializeField] private Color glowColor = new Color(0.2f, 0.85f, 1f, 0.7f);
        [SerializeField] private Color coreColor = new Color(0.85f, 1f, 1f, 1f);
        [SerializeField] private float dotGlowSize = 40f;
        [SerializeField] private float dotCoreSize = 14f;
        [SerializeField] private float boneGlowWidth = 24f;
        [SerializeField] private float boneCoreWidth = 7f;
        [SerializeField] private float ringGlowWidth = 20f;
        [SerializeField] private float ringCoreWidth = 6f;

        private static Texture2D _glowTexture;

        private PoseSmoother _source;
        private float _drawnPresence = -1f;

        public override Texture mainTexture => GlowTexture;

        public static Texture2D GlowTexture
        {
            get
            {
                if (_glowTexture == null) _glowTexture = CreateGlowTexture();
                return _glowTexture;
            }
        }

        public void SetSource(PoseSmoother source)
        {
            _source = source;
            _drawnPresence = -1f;
            SetVerticesDirty();
        }

        /// <summary>Marks the mesh dirty unless the figure is and was fully transparent.</summary>
        public void Refresh()
        {
            float presence = _source != null ? _source.Presence : 0f;
            if (presence <= 0f && _drawnPresence == 0f) return;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float presence = _source != null && _source.Settled ? _source.Presence : 0f;
            _drawnPresence = presence;
            if (presence <= 0f) return;

            Rect r = rectTransform.rect;
            float unit = r.height * unitPerHeight;
            Vector2 origin = new Vector2(r.xMin + r.width * centerInRect.x, r.yMin + r.height * centerInRect.y);
            float px = unit / 100f;

            // Glow first so the cores sit on top.
            AddFigure(vh, origin, unit, presence, glowColor, dotGlowSize * px, boneGlowWidth * px, ringGlowWidth * px);
            AddFigure(vh, origin, unit, presence, coreColor, dotCoreSize * px, boneCoreWidth * px, ringCoreWidth * px);
        }

        private void AddFigure(VertexHelper vh, Vector2 origin, float unit, float presence, Color baseColor,
            float dotSize, float boneWidth, float ringWidth)
        {
            PoseSmoother s = _source;

            for (int b = 0; b < BoneCount; b++)
            {
                PoseJoint a = Bones[b * 2];
                PoseJoint c = Bones[b * 2 + 1];
                Vector2 p0 = origin + s.Joint(a) * unit;
                Vector2 p1 = origin + s.Joint(c) * unit;
                AddLine(vh, p0, p1, boneWidth, Tint(baseColor, s.BoneAlpha(a, c) * presence));
            }

            Vector2 head = origin + s.Joint(PoseJoint.Nose) * unit;
            Color headColor = Tint(baseColor, s.JointAlpha(PoseJoint.Nose) * presence);
            AddRing(vh, head, headRadius * unit, ringWidth, headColor);

            for (int j = 0; j < PoseProtocol.JointCount; j++)
            {
                Vector2 p = origin + s.Joint(j) * unit;
                float size = j == (int)PoseJoint.Nose ? dotSize * 0.6f : dotSize;
                AddDot(vh, p, size * 0.5f, Tint(baseColor, s.JointAlpha(j) * presence));
            }
        }

        private static Color32 Tint(Color c, float alpha)
        {
            c.a *= alpha;
            return c;
        }

        private static void AddDot(VertexHelper vh, Vector2 c, float half, Color32 color)
        {
            if (color.a == 0) return;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(c.x - half, c.y - half), color, new Vector4(0f, 0f));
            vh.AddVert(new Vector3(c.x - half, c.y + half), color, new Vector4(0f, 1f));
            vh.AddVert(new Vector3(c.x + half, c.y + half), color, new Vector4(1f, 1f));
            vh.AddVert(new Vector3(c.x + half, c.y - half), color, new Vector4(1f, 0f));
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        // Samples the texture's middle row across the width, so the glow profile runs across the line.
        private static void AddLine(VertexHelper vh, Vector2 p0, Vector2 p1, float width, Color32 color)
        {
            if (color.a == 0) return;
            Vector2 d = p1 - p0;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f / len);

            int i = vh.currentVertCount;
            vh.AddVert(p0 - n, color, new Vector4(0f, 0.5f));
            vh.AddVert(p0 + n, color, new Vector4(1f, 0.5f));
            vh.AddVert(p1 + n, color, new Vector4(1f, 0.5f));
            vh.AddVert(p1 - n, color, new Vector4(0f, 0.5f));
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        // Annulus strip sharing vertices between segments (no overlap seams). u runs inner (0) to outer (1).
        private static void AddRing(VertexHelper vh, Vector2 c, float radius, float width, Color32 color)
        {
            if (color.a == 0) return;
            float inner = Mathf.Max(0f, radius - width * 0.5f);
            float outer = radius + width * 0.5f;
            int start = vh.currentVertCount;
            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i * (Mathf.PI * 2f / RingSegments);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(c + dir * inner, color, new Vector4(0f, 0.5f));
                vh.AddVert(c + dir * outer, color, new Vector4(1f, 0.5f));
            }

            for (int i = 0; i < RingSegments; i++)
            {
                int a = start + i * 2;
                vh.AddTriangle(a, a + 1, a + 3);
                vh.AddTriangle(a + 3, a + 2, a);
            }
        }

        /// <summary>White with a radial alpha falloff: bright core, soft tail. Created once.</summary>
        private static Texture2D CreateGlowTexture()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, true)
            {
                name = "NeonGlow (runtime)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color32[TextureSize * TextureSize];
            float half = (TextureSize - 1) * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(1f - r);
                    // Soft tail plus a brighter core. Not Mathf.SmoothStep: that one lerps between its first two arguments.
                    float a = Mathf.Clamp01(t * t * 0.7f + SmoothStep01(0.45f, 0.75f, t) * 0.6f);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>GLSL-style smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>.</summary>
        public static float SmoothStep01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
