using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.UI;

namespace VisionToWall.PoseTest.Editor
{
    /// <summary>
    /// After <c>pose_from_media.py --all Temp/pose_check</c>: copies each clip's <c>truth.csv</c> / <c>detected.csv</c>
    /// into <see cref="DataDir"/> (committed), evaluates them with <see cref="PoseCheck"/>, draws comparison images
    /// and writes <c>Temp/pose_check/report.md</c>.
    /// <para>
    /// Comparison image (all panels mirrored, as the player sees the wall): [synthetic frame | detection (green) and
    /// truth (red) over the dimmed frame | mini character]. The mini character panel redraws the
    /// <see cref="PoseSmoother"/> output with the <see cref="NeonFigureGraphic"/> layout (bones, joints, head ring,
    /// alpha), not the UI prefab itself.
    /// </para>
    /// </summary>
    public static class PoseTestReport
    {
        public const string DataDir = "Assets/02_Scripts/PoseTest/Tests/Data";
        public const int ImagesPerClip = 6;
        private const int W = PoseTestRenderer.Width;
        private const int H = PoseTestRenderer.Height;

        [MenuItem("VisionToWall/PoseTest/2. Build Report (after pose_from_media.py)")]
        public static void BuildMenu() => Debug.Log("[PoseTest] report: " + Build(true));

        /// <summary>Returns the report path. Clips without data are listed as missing.</summary>
        public static string Build(bool images)
        {
            Directory.CreateDirectory(DataDir);
            var results = new List<ClipCheck>();
            var missing = new List<string>();
            foreach (PoseClip clip in PoseTestClips.All)
            {
                string dir = Path.Combine(PoseTestRenderer.OutputRoot, clip.Name);
                string truthTemp = Path.Combine(dir, "truth.csv");
                string detTemp = Path.Combine(dir, "detected.csv");
                if (File.Exists(truthTemp) && File.Exists(detTemp) &&
                    File.GetLastWriteTimeUtc(detTemp) >= File.GetLastWriteTimeUtc(truthTemp))
                {
                    File.Copy(truthTemp, TruthPath(clip.Name), true);
                    File.Copy(detTemp, DetectedPath(clip.Name), true);
                }

                if (!File.Exists(TruthPath(clip.Name)) || !File.Exists(DetectedPath(clip.Name)))
                {
                    missing.Add(clip.Name);
                    continue;
                }

                PoseTruth truth = PoseTruth.Parse(File.ReadAllText(TruthPath(clip.Name)));
                PoseRecording det = PoseRecording.Parse(File.ReadAllText(DetectedPath(clip.Name)));

                int[] picks = PickFrames(truth.Count);
                var snaps = new Snapshot[picks.Length];
                PoseCheck.TickProbe probe = (t, s, last) =>
                {
                    for (int k = 0; k < picks.Length; k++)
                        if (snaps[k] == null && t + 1e-4f >= PoseTestRenderer.FrameMs(picks[k], PoseTestClips.Fps) / 1000f)
                            snaps[k] = Snapshot.Of(s);
                };
                ClipCheck r = PoseCheck.Evaluate(clip, truth.Joints, det, probe);
                results.Add(r);
                if (images && Directory.Exists(dir)) WriteImages(dir, clip, truth.Joints, det, picks, snaps);
            }

            AssetDatabase.Refresh();
            string report = Path.Combine(PoseTestRenderer.OutputRoot, "report.md");
            Directory.CreateDirectory(PoseTestRenderer.OutputRoot);
            File.WriteAllText(report, Markdown(results, missing));
            return report;
        }

        public static string TruthPath(string clip) => Path.Combine(DataDir, clip + "_truth.csv");
        public static string DetectedPath(string clip) => Path.Combine(DataDir, clip + "_detected.csv");

        private static int[] PickFrames(int count)
        {
            var picks = new int[ImagesPerClip];
            for (int k = 0; k < ImagesPerClip; k++)
                picks[k] = Mathf.Clamp(Mathf.RoundToInt((k + 0.5f) / ImagesPerClip * (count - 1)), 0, count - 1);
            return picks;
        }

        // ------------------------------------------------------------------ report text

        private static string Markdown(List<ClipCheck> results, List<string> missing)
        {
            var sb = new StringBuilder();
            sb.Append("# Pose Test Rig 리포트\n\n");
            sb.Append("- 생성: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("- 모델: `PlayerAvatar_Rig.fbx` (Humanoid), 키 1.7 m, 카메라 3 m 정면, 높이 1.05 m, 세로 화각 43°, 640×480, 30 fps\n");
            sb.Append("- 재질: 회색 0.55 URP Lit, 텍스처 없음, 배경 (0.12, 0.12, 0.13)\n");
            sb.Append("- 인식: `pose_from_media.py` (손 센서와 같은 PoseLandmarker lite, VIDEO 모드, 신뢰도 0.5, 미러)\n");
            sb.Append("- 인형: R3 `PoseSmoother` 60 fps 재생\n");
            sb.Append("- 각도: 인식 vs 정답은 월드 3D, 인형 vs 인식은 월드 x·y 2D. 팔꿈치·무릎은 안쪽 각, 어깨는 골반-어깨-팔꿈치\n");
            sb.Append("- 기준 (56번 4절): 검출률 ≥ 90%, 각도 오차 중앙값 ≤ 15°, 좌우 뒤바뀜 0, 인형 각도 중앙값 ≤ 15°, 걸어 나가면 사라지고 돌아오면 나타남\n\n");

            sb.Append("| 클립 | 판정 | 검출률 | 팔꿈치 중앙/최대 | 무릎 중앙/최대 | 어깨 중앙/최대 | 좌우 뒤바뀜 | 인형 팔꿈치/무릎/어깨 중앙 |\n");
            sb.Append("|---|---|---|---|---|---|---|---|\n");
            foreach (ClipCheck r in results)
            {
                sb.Append("| ").Append(r.Clip).Append(" | ").Append(r.Pass ? "PASS" : "**FAIL**").Append(" | ")
                    .Append(Pct(r.DetectionRate)).Append($" ({r.DetectedFull}/{r.FullFrames})").Append(" | ")
                    .Append(MedMax(r.Elbow)).Append(" | ").Append(MedMax(r.Knee)).Append(" | ").Append(MedMax(r.Shoulder)).Append(" | ")
                    .Append(r.SwapFrames).Append(" | ")
                    .Append(Deg(r.FigElbow.Median)).Append(" / ").Append(Deg(r.FigKnee.Median)).Append(" / ").Append(Deg(r.FigShoulder.Median))
                    .Append(" |\n");
            }

            if (missing.Count > 0) sb.Append("\n데이터 없음: ").Append(string.Join(", ", missing)).Append('\n');

            sb.Append("\n### 원인 분리 (참고, 판정에 쓰지 않음)\n\n");
            sb.Append("정면 2D = 같은 각도를 월드 x·y만으로 계산 (깊이 z 제외, 미니 캐릭터가 그리는 면). ");
            sb.Append("깊이 오차 = 골반 중심 대비 z의 (인식 − 정답) 중앙값, + = 카메라에서 먼 쪽.\n\n");
            sb.Append("| 클립 | 정면 2D 팔꿈치 | 정면 2D 무릎 | 정면 2D 어깨 | 깊이 오차 어깨 / 손목 / 발목 |\n|---|---|---|---|---|\n");
            foreach (ClipCheck r in results)
            {
                sb.Append("| ").Append(r.Clip).Append(" | ").Append(MedMax(r.Elbow2D)).Append(" | ").Append(MedMax(r.Knee2D))
                    .Append(" | ").Append(MedMax(r.Shoulder2D)).Append(" | ")
                    .Append(Cm(r.DepthShoulder)).Append(" / ").Append(Cm(r.DepthWrist)).Append(" / ").Append(Cm(r.DepthAnkle)).Append(" |\n");
            }

            sb.Append("\n## 클립별 상세\n");
            foreach (ClipCheck r in results)
            {
                PoseClip clip = PoseTestClips.Find(r.Clip);
                sb.Append("\n### ").Append(r.Clip).Append(r.Pass ? " — PASS" : " — FAIL").Append('\n');
                sb.Append("- 동작: ").Append(clip.Description).Append('\n');
                sb.Append($"- 프레임 {r.Frames}: 전신 화면 안 {r.FullFrames}, 일부 {r.PartialFrames} (검출 {r.DetectedPartial}), 화면 밖 {r.OutFrames} (검출 {r.DetectedOut})\n");
                foreach (string f in r.Failures) sb.Append("- 실패: ").Append(f).Append('\n');
                sb.Append("- 인형 vs 정답 (전체 각도, 참고): ").Append(MedMax(r.FigTruth)).Append('\n');
                if (!float.IsNaN(r.OnsetTruth))
                {
                    sb.Append($"- 지연 (어깨 각도, {(clip.Arm == ActiveArm.Right ? "오른팔" : "왼팔")}): 시작(+{PoseCheck.OnsetDeg:F0}°) 인식 {Ms(r.OnsetDelayDetected)}, 인형 {Ms(r.OnsetDelayFigure)}; ");
                    sb.Append($"절반 지점 인식 {Ms(r.HalfDelayDetected)}, 인형 {Ms(r.HalfDelayFigure)}. 카메라 노출·전송·화면 출력 제외\n");
                }

                sb.Append($"- 떨림 (코·어깨·골반, 프레임당 몸통 단위): 정답 {F3(r.JitterTruth)}, 인식 {F3(r.JitterDetected)}, 인형 {F3(r.JitterFigure)} (패널 {Px(r.JitterFigure)}, 최대 {Px(r.JitterFigureMax)})\n");
                if (clip.LeavesFrame)
                {
                    sb.Append($"- 퇴장: {(r.Disappeared ? "사라짐, 몸이 화면을 벗어난 뒤 " + Sec(r.DisappearSec) : "사라지지 않음")}; ");
                    sb.Append($"등장: {(r.Reappeared ? "나타남, 전신이 들어온 뒤 " + Sec(r.ReappearSec) : "나타나지 않음")}; ");
                    sb.Append($"몸 일부만 보이는(양 어깨는 화면 안) 프레임에서 인형 표시 {Pct(r.PartialShownRatio)}\n");
                }

                sb.Append($"- 비교 이미지: `{r.Clip}/sheet.png` (프레임별 `{r.Clip}/compare_NNNNN.png`)\n");
            }

            return sb.ToString();
        }

        private static string Pct(float v) => float.IsNaN(v) ? "-" : (v * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%";
        private static string Deg(float v) => float.IsNaN(v) ? "-" : v.ToString("F1", CultureInfo.InvariantCulture) + "°";
        private static string MedMax(ErrorStat s) => s.Count == 0 ? "-" : Deg(s.Median) + " / " + Deg(s.Max);
        private static string Cm(float m) => float.IsNaN(m) ? "-" : (m * 100f).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " cm";
        private static string F3(float v) => float.IsNaN(v) ? "-" : v.ToString("F4", CultureInfo.InvariantCulture);
        private static string Px(float v) => float.IsNaN(v) ? "-" : (v * PoseCheck.PanelPxPerUnit).ToString("F1", CultureInfo.InvariantCulture) + " px";

        private static string Ms(float sec) =>
            float.IsNaN(sec) ? "-" : $"{sec * 1000f:F0} ms ({sec * PoseTestClips.Fps:F1}프레임)";

        private static string Sec(float sec) => float.IsNaN(sec) ? "-" : sec.ToString("F2", CultureInfo.InvariantCulture) + "초";

        // ------------------------------------------------------------------ images

        private sealed class Snapshot
        {
            public readonly Vector2[] Joints = new Vector2[PoseProtocol.JointCount];
            public readonly float[] Alpha = new float[PoseProtocol.JointCount];
            public float Presence;

            public static Snapshot Of(PoseSmoother s)
            {
                var snap = new Snapshot { Presence = s.Settled ? s.Presence : 0f };
                for (int j = 0; j < PoseProtocol.JointCount; j++)
                {
                    snap.Joints[j] = s.Joint(j);
                    snap.Alpha[j] = s.JointAlpha(j);
                }

                return snap;
            }
        }

        private static void WriteImages(string dir, PoseClip clip, PoseRecording truth, PoseRecording det, int[] picks, Snapshot[] snaps)
        {
            foreach (string old in Directory.GetFiles(dir, "compare_*.png")) File.Delete(old);
            var frameTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var rows = new List<Color32[]>();
            var tf = new PoseFrame();
            var df = new PoseFrame();
            for (int k = 0; k < picks.Length; k++)
            {
                int i = picks[k];
                string png = Path.Combine(dir, $"frame_{i:D5}.png");
                if (!File.Exists(png)) continue;
                frameTex.LoadImage(File.ReadAllBytes(png));
                Color32[] src = frameTex.GetPixels32();
                truth.CopyTo(i, tf, 0);
                det.CopyTo(i, df, 0);

                var img = new Raster(W * 3, H);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        Color32 c = src[y * W + (W - 1 - x)]; // mirror
                        img.Set(x, y, c);
                        img.Set(W + x, y, new Color32((byte)(c.r * 0.45f), (byte)(c.g * 0.45f), (byte)(c.b * 0.45f), 255));
                    }

                if (tf.HasPerson) DrawSkeleton(img, W, tf, new Color(1f, 0.25f, 0.2f), false);
                if (df.HasPerson) DrawSkeleton(img, W, df, new Color(0.2f, 1f, 0.3f), true);
                DrawFigure(img, 2 * W, snaps[k]);

                byte[] bytes = img.ToPng();
                File.WriteAllBytes(Path.Combine(dir, $"compare_{i:D5}.png"), bytes);
                rows.Add(img.Half());
            }

            if (rows.Count > 0)
            {
                int rw = W * 3 / 2, rh = H / 2;
                var sheet = new Raster(rw, rh * rows.Count);
                for (int r = 0; r < rows.Count; r++)
                {
                    int yBase = (rows.Count - 1 - r) * rh; // first row on top
                    for (int y = 0; y < rh; y++)
                        Array.Copy(rows[r], y * rw, sheet.Pixels, (yBase + y) * rw, rw);
                }

                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), sheet.ToPng());
            }

            UnityEngine.Object.DestroyImmediate(frameTex);
        }

        private static void DrawSkeleton(Raster img, int x0, PoseFrame f, Color color, bool dimLowVisibility)
        {
            PoseJoint[] bones = NeonFigureGraphic.Bones;
            for (int b = 0; b < bones.Length; b += 2)
            {
                int a = (int)bones[b], c = (int)bones[b + 1];
                float alpha = dimLowVisibility && Mathf.Min(f.Visibility[a], f.Visibility[c]) < PoseSmoother.VisibleThreshold ? 0.35f : 1f;
                img.Line(x0 + f.Screen[a].x * W, f.Screen[a].y * H, x0 + f.Screen[c].x * W, f.Screen[c].y * H, 2f, color, alpha);
            }

            for (int j = 0; j < PoseProtocol.JointCount; j++)
            {
                float alpha = dimLowVisibility && f.Visibility[j] < PoseSmoother.VisibleThreshold ? 0.35f : 1f;
                img.Disc(x0 + f.Screen[j].x * W, f.Screen[j].y * H, j == 0 ? 5f : 4f, color, alpha);
            }
        }

        /// <summary>Same layout as NeonFigureGraphic defaults: center (0.5, 0.56), unit = 0.22 x height, head ring 0.3.</summary>
        private static void DrawFigure(Raster img, int x0, Snapshot s)
        {
            var bg = new Color32(8, 12, 24, 255);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    img.Set(x0 + x, y, bg);
            if (s == null || s.Presence <= 0f) return;

            float unit = H * 0.22f;
            var origin = new Vector2(x0 + W * 0.5f, H * 0.56f);
            var glow = new Color(0.2f, 0.85f, 1f);
            var core = new Color(0.85f, 1f, 1f);
            Vector2 P(int j) => origin + s.Joints[j] * unit;

            float shoulderY = (s.Joints[(int)PoseJoint.LeftShoulder].y + s.Joints[(int)PoseJoint.RightShoulder].y) * 0.5f;
            Vector2 nose = s.Joints[(int)PoseJoint.Nose];
            nose.y = Mathf.Max(nose.y, shoulderY + 0.3f + 0.1f);
            Vector2 head = origin + nose * unit;

            for (int pass = 0; pass < 2; pass++)
            {
                Color c = pass == 0 ? glow : core;
                float width = pass == 0 ? 9f : 3f;
                float a0 = pass == 0 ? 0.55f : 1f;
                PoseJoint[] bones = NeonFigureGraphic.Bones;
                for (int b = 0; b < bones.Length; b += 2)
                {
                    int a = (int)bones[b], d = (int)bones[b + 1];
                    float alpha = Mathf.Min(s.Alpha[a], s.Alpha[d]) * s.Presence * a0;
                    Vector2 p0 = P(a), p1 = P(d);
                    img.Line(p0.x, p0.y, p1.x, p1.y, width, c, alpha);
                }

                img.Ring(head.x, head.y, 0.3f * unit, width, c, s.Alpha[(int)PoseJoint.Nose] * s.Presence * a0);
                for (int j = 1; j < PoseProtocol.JointCount; j++)
                {
                    Vector2 p = P(j);
                    img.Disc(p.x, p.y, pass == 0 ? 10f : 4f, c, s.Alpha[j] * s.Presence * a0);
                }
            }
        }

        /// <summary>Tiny software raster (origin bottom-left, like Texture2D).</summary>
        private sealed class Raster
        {
            public readonly int Width, Height;
            public readonly Color32[] Pixels;

            public Raster(int w, int h)
            {
                Width = w;
                Height = h;
                Pixels = new Color32[w * h];
            }

            public void Set(int x, int y, Color32 c)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return;
                Pixels[y * Width + x] = c;
            }

            private void Blend(int x, int y, Color c, float a)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height || a <= 0f) return;
                int i = y * Width + x;
                Color32 d = Pixels[i];
                Pixels[i] = new Color32(
                    (byte)Mathf.Clamp(d.r + (c.r * 255f - d.r) * a, 0f, 255f),
                    (byte)Mathf.Clamp(d.g + (c.g * 255f - d.g) * a, 0f, 255f),
                    (byte)Mathf.Clamp(d.b + (c.b * 255f - d.b) * a, 0f, 255f), 255);
            }

            public void Disc(float cx, float cy, float r, Color c, float a)
            {
                for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                    for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        Blend(x, y, c, a * Mathf.Clamp01(r + 0.5f - d));
                    }
            }

            public void Line(float x0, float y0, float x1, float y1, float width, Color c, float a)
            {
                float r = width * 0.5f;
                int minX = Mathf.FloorToInt(Mathf.Min(x0, x1) - r), maxX = Mathf.CeilToInt(Mathf.Max(x0, x1) + r);
                int minY = Mathf.FloorToInt(Mathf.Min(y0, y1) - r), maxY = Mathf.CeilToInt(Mathf.Max(y0, y1) + r);
                var p0 = new Vector2(x0, y0);
                Vector2 seg = new Vector2(x1, y1) - p0;
                float len2 = Mathf.Max(seg.sqrMagnitude, 1e-6f);
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        var p = new Vector2(x, y);
                        float u = Mathf.Clamp01(Vector2.Dot(p - p0, seg) / len2);
                        float d = Vector2.Distance(p, p0 + seg * u);
                        Blend(x, y, c, a * Mathf.Clamp01(r + 0.5f - d));
                    }
            }

            public void Ring(float cx, float cy, float radius, float width, Color c, float a)
            {
                float r = width * 0.5f;
                for (int y = Mathf.FloorToInt(cy - radius - r); y <= Mathf.CeilToInt(cy + radius + r); y++)
                    for (int x = Mathf.FloorToInt(cx - radius - r); x <= Mathf.CeilToInt(cx + radius + r); x++)
                    {
                        float d = Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius);
                        Blend(x, y, c, a * Mathf.Clamp01(r + 0.5f - d));
                    }
            }

            public Color32[] Half()
            {
                int w = Width / 2, h = Height / 2;
                var o = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Color32 a = Pixels[2 * y * Width + 2 * x], b = Pixels[2 * y * Width + 2 * x + 1];
                        Color32 c = Pixels[(2 * y + 1) * Width + 2 * x], d = Pixels[(2 * y + 1) * Width + 2 * x + 1];
                        o[y * w + x] = new Color32((byte)((a.r + b.r + c.r + d.r) / 4), (byte)((a.g + b.g + c.g + d.g) / 4),
                            (byte)((a.b + b.b + c.b + d.b) / 4), 255);
                    }

                return o;
            }

            public byte[] ToPng()
            {
                // CPU only (no texture upload; see PoseTestRenderer.Stage.RenderPng).
                var rgba = new byte[Pixels.Length * 4];
                for (int i = 0; i < Pixels.Length; i++)
                {
                    rgba[i * 4] = Pixels[i].r;
                    rgba[i * 4 + 1] = Pixels[i].g;
                    rgba[i * 4 + 2] = Pixels[i].b;
                    rgba[i * 4 + 3] = 255;
                }

                return ImageConversion.EncodeArrayToPNG(rgba, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                    (uint)Width, (uint)Height);
            }
        }
    }
}
