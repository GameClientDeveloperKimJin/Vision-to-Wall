using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VisionToWall.Core;
using VisionToWall.UI;

namespace VisionToWall.Game.Dev
{
    /// <summary>
    /// R4b comparison of the player's motion (camera 2D joints) and the combat avatar's motion (doc 57).
    /// A pose recording (<c>joints.csv</c>, the sensor's packets) is replayed at 60 fps through the same
    /// <see cref="AvatarPoseSolver"/> and <see cref="AvatarSkeleton"/> as the game, on the real rig. Per packet and limb:
    /// <list type="bullet">
    /// <item><b>angle</b>: avatar bone seen from behind (root frame x, y) vs the camera 2D limb (mirror applied,
    /// pixels). Only when both joints are visible, the limb is tracked and the 2D limb is not foreshortened.</item>
    /// <item><b>length</b>: avatar bone frontal length (|dir.xy|, 1 = in the screen plane) vs the 2D limb length over
    /// the longest seen. Big positive = the avatar's limb lies in the screen plane while the real one points at
    /// the camera (depth).</item>
    /// <item>cause hints: raw world joints vs camera 2D (recognition: MediaPipe world disagrees with the image),
    /// best match within 200 ms earlier (smoothing lag), the other side's limb (left/right).</item>
    /// </list>
    /// </summary>
    public static class AvatarCompare
    {
        public const string ModelPath = "Assets/07_Models/Avatar/PlayerAvatar_Rig.fbx";
        public const float FrameDt = 1f / 60f;
        public const float CameraAspect = 640f / 480f;
        public const float ShortRatio = 0.5f;
        public const float LagWindowSec = 0.2f;
        public const float EdgeMargin = 0.02f;

        public static readonly string[] LimbNames = { "LUpperArm", "LForeArm", "RUpperArm", "RForeArm", "LThigh", "LShin", "RThigh", "RShin" };

        /// <summary>Joints drawn for the avatar panel, root frame x, y.</summary>
        public static readonly string[] AvatarPoints =
        {
            "Head", "LShoulder", "RShoulder", "LElbow", "RElbow", "LWrist", "RWrist",
            "LHip", "RHip", "LKnee", "RKnee", "LAnkle", "RAnkle",
        };

        public sealed class LimbSample
        {
            public bool Visible;      // both joints visibility >= 0.5
            public bool OffFrame;     // a joint at the image edge (clamped 2D: no valid reference, framing)
            public float Weight;      // avatar limb tracking weight
            public bool Short;        // 2D limb shorter than ShortRatio of its longest
            public float CamDeg = float.NaN;
            public float AvatarDeg = float.NaN;
            public float WorldDeg = float.NaN;
            public float Angle = float.NaN;      // |avatar - cam|
            public float WorldAngle = float.NaN; // |raw world - cam|
            public float LagAngle = float.NaN;   // best |avatar - cam(t - 0..200 ms)|
            public float SwapAngle = float.NaN;  // |avatar - other side's cam|
            public float CamRatio = float.NaN;   // 2D length / longest seen
            public float AvatarFrontal = float.NaN;
        }

        public sealed class Frame
        {
            public uint TimeMs;
            public bool HasPerson;
            public readonly LimbSample[] Limbs = new LimbSample[AvatarPoseSolver.LimbCount];
            public readonly Vector2[] Points = new Vector2[13];
        }

        public sealed class Rig : IDisposable
        {
            public readonly Scene Scene;
            public readonly GameObject Root;
            public readonly Animator Animator;
            public readonly AvatarSkeleton Skeleton;

            public Rig()
            {
                Scene = EditorSceneManager.NewPreviewScene();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Root = UnityEngine.Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(Root, Scene);
                Animator = Root.GetComponent<Animator>();
                Skeleton = new AvatarSkeleton(Root.transform, Animator, Root.GetComponentInChildren<SkinnedMeshRenderer>(),
                    Quaternion.Euler(270f, 0f, 0f));
            }

            public void Dispose() => EditorSceneManager.ClosePreviewScene(Scene);
        }

        public static List<Frame> Run(PoseRecording rec, AvatarPoseSolver solver, Rig rig)
        {
            var frames = new List<Frame>();
            var frame = new PoseFrame();
            var longest = new float[AvatarPoseSolver.LimbCount];
            var history = new List<KeyValuePair<float, float[]>>(); // (time, cam deg per limb)
            int next = 0;
            float lastPacket = -1f;
            float end = rec.DurationSec;
            for (float t = 0f; t <= end + 1e-4f; t += FrameDt)
            {
                bool newPacket = false;
                while (next < rec.Count && rec[next].TimestampMs / 1000f <= t + 1e-4f)
                {
                    rec.CopyTo(next, frame, (uint)next);
                    lastPacket = rec[next].TimestampMs / 1000f;
                    next++;
                    newPacket = true;
                }

                solver.Update(lastPacket < 0f ? null : frame, lastPacket < 0f ? 99f : t - lastPacket, t, FrameDt);
                if (!newPacket) continue;

                rig.Skeleton.Apply(solver);
                var f = new Frame { TimeMs = frame.TimestampMs, HasPerson = frame.HasPerson };
                SamplePoints(rig, f.Points);
                var camDeg = new float[AvatarPoseSolver.LimbCount];
                for (int i = 0; i < AvatarPoseSolver.LimbCount; i++)
                {
                    var s = new LimbSample();
                    f.Limbs[i] = s;
                    camDeg[i] = float.NaN;
                    var limb = (AvatarLimb)i;
                    s.Weight = solver.Weight(limb);
                    Vector3 dir = rig.Skeleton.LimbDirection(limb);
                    s.AvatarDeg = Deg(dir.x, dir.y);
                    s.AvatarFrontal = new Vector2(dir.x, dir.y).magnitude;
                    if (!frame.HasPerson) continue;

                    int a = (int)AvatarPoseSolver.LimbFrom[i], b = (int)AvatarPoseSolver.LimbTo[i];
                    s.Visible = frame.Visibility[a] >= AvatarPoseSolver.VisibleThreshold && frame.Visibility[b] >= AvatarPoseSolver.VisibleThreshold;
                    Vector2 cam = (frame.Screen[b] - frame.Screen[a]);
                    cam.x *= CameraAspect;
                    if (s.Visible) longest[i] = Mathf.Max(longest[i], cam.magnitude);
                    s.CamRatio = longest[i] > 1e-5f ? cam.magnitude / longest[i] : float.NaN;
                    s.Short = s.CamRatio < ShortRatio;
                    s.OffFrame = !Inside(frame.Screen[a]) || !Inside(frame.Screen[b]);
                    s.CamDeg = Deg(cam.x, cam.y);
                    camDeg[i] = s.Visible ? s.CamDeg : float.NaN;
                    Vector3 w = frame.World[b] - frame.World[a];
                    s.WorldDeg = Deg(w.x, w.y);
                    if (!s.Visible || s.Short || s.OffFrame) continue;

                    s.Angle = Diff(s.AvatarDeg, s.CamDeg);
                    s.WorldAngle = Diff(s.WorldDeg, s.CamDeg);
                }

                // other side and lag (camera directions only exist for this packet now)
                for (int i = 0; i < AvatarPoseSolver.LimbCount; i++)
                {
                    LimbSample s = f.Limbs[i];
                    if (float.IsNaN(s.Angle)) continue;
                    int other = i < 4 ? (i + 2) % 4 : 4 + (i - 4 + 2) % 4;
                    if (!float.IsNaN(camDeg[other])) s.SwapAngle = Diff(s.AvatarDeg, camDeg[other]);
                    float best = s.Angle;
                    for (int h = history.Count - 1; h >= 0 && t - history[h].Key <= LagWindowSec; h--)
                        if (!float.IsNaN(history[h].Value[i])) best = Mathf.Min(best, Diff(s.AvatarDeg, history[h].Value[i]));
                    s.LagAngle = best;
                }

                history.Add(new KeyValuePair<float, float[]>(t, camDeg));
                if (history.Count > 30) history.RemoveAt(0);
                frames.Add(f);
            }

            rig.Skeleton.ApplyRest();
            return frames;
        }

        private static void SamplePoints(Rig rig, Vector2[] points)
        {
            Transform root = rig.Root.transform;
            Vector2 P(Transform t) => (Vector2)root.InverseTransformPoint(t.position);
            AvatarSkeleton s = rig.Skeleton;
            points[0] = P(rig.Animator.GetBoneTransform(HumanBodyBones.Head));
            points[1] = P(s.Bone(AvatarLimb.LeftUpperArm));
            points[2] = P(s.Bone(AvatarLimb.RightUpperArm));
            points[3] = P(s.Bone(AvatarLimb.LeftLowerArm));
            points[4] = P(s.Bone(AvatarLimb.RightLowerArm));
            points[5] = P(s.BoneEnd(AvatarLimb.LeftLowerArm));
            points[6] = P(s.BoneEnd(AvatarLimb.RightLowerArm));
            points[7] = P(s.Bone(AvatarLimb.LeftUpperLeg));
            points[8] = P(s.Bone(AvatarLimb.RightUpperLeg));
            points[9] = P(s.Bone(AvatarLimb.LeftLowerLeg));
            points[10] = P(s.Bone(AvatarLimb.RightLowerLeg));
            points[11] = P(s.BoneEnd(AvatarLimb.LeftLowerLeg));
            points[12] = P(s.BoneEnd(AvatarLimb.RightLowerLeg));
        }

        private static bool Inside(Vector2 p) =>
            p.x >= EdgeMargin && p.x <= 1f - EdgeMargin && p.y >= EdgeMargin && p.y <= 1f - EdgeMargin;

        private static float Deg(float x, float y) => Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        private static float Diff(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b));

        // ---------- output ----------

        public static string ToCsv(List<Frame> frames)
        {
            var sb = new StringBuilder("t_ms,person");
            foreach (string p in AvatarPoints) sb.Append(',').Append(p).Append("_x,").Append(p).Append("_y");
            foreach (string l in LimbNames)
                sb.Append(',').Append(l).Append("_vis,").Append(l).Append("_w,").Append(l).Append("_short,").Append(l).Append("_off,")
                  .Append(l).Append("_angle,").Append(l).Append("_world,").Append(l).Append("_lag,").Append(l).Append("_swap,")
                  .Append(l).Append("_camRatio,").Append(l).Append("_avatarFrontal");
            sb.Append('\n');
            foreach (Frame f in frames)
            {
                sb.Append(f.TimeMs).Append(',').Append(f.HasPerson ? 1 : 0);
                foreach (Vector2 p in f.Points) sb.Append(',').Append(F(p.x)).Append(',').Append(F(p.y));
                foreach (LimbSample s in f.Limbs)
                    sb.Append(',').Append(s.Visible ? 1 : 0).Append(',').Append(F(s.Weight)).Append(',').Append(s.Short ? 1 : 0).Append(',').Append(s.OffFrame ? 1 : 0)
                      .Append(',').Append(F(s.Angle)).Append(',').Append(F(s.WorldAngle)).Append(',').Append(F(s.LagAngle))
                      .Append(',').Append(F(s.SwapAngle)).Append(',').Append(F(s.CamRatio)).Append(',').Append(F(s.AvatarFrontal));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Angle median, 95th pct and share above 25 (on-frame limbs), depth median, share of visible limbs at the edge.</summary>
        public static (float median, float p95, float over25, float depthMedian, float offFrame) Summary(List<Frame> frames)
        {
            var angles = new List<float>();
            var depth = new List<float>();
            int visible = 0, off = 0;
            foreach (Frame f in frames)
                foreach (LimbSample s in f.Limbs)
                {
                    if (s.Visible)
                    {
                        visible++;
                        if (s.OffFrame) off++;
                    }
                    if (!float.IsNaN(s.Angle)) angles.Add(s.Angle);
                    if (s.Visible && s.Weight >= 0.99f && s.Short) depth.Add(s.AvatarFrontal - s.CamRatio);
                }
            angles.Sort();
            depth.Sort();
            float Pct(List<float> v, float p) => v.Count == 0 ? float.NaN : v[Mathf.Clamp(Mathf.RoundToInt(p * (v.Count - 1)), 0, v.Count - 1)];
            int over = 0;
            foreach (float a in angles) if (a > 25f) over++;
            return (Pct(angles, 0.5f), Pct(angles, 0.95f), angles.Count == 0 ? 0f : (float)over / angles.Count, Pct(depth, 0.5f),
                visible == 0 ? 0f : (float)off / visible);
        }

        private static string F(float v) => float.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
