using System;
using System.Collections.Generic;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.UI;

namespace VisionToWall.PoseTest
{
    public struct ErrorStat
    {
        public int Count;
        public float Median;
        public float Max;

        public static ErrorStat From(List<float> values)
        {
            var s = new ErrorStat { Count = values.Count, Median = float.NaN, Max = float.NaN };
            if (values.Count == 0) return s;
            values.Sort();
            int n = values.Count;
            s.Median = n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) * 0.5f;
            s.Max = values[n - 1];
            return s;
        }
    }

    /// <summary>Numbers of one clip (spec 56 4절). Angles in degrees, times in seconds.</summary>
    public sealed class ClipCheck
    {
        public string Clip;
        public int Frames;
        /// <summary>Truth frames with all 13 joints inside the camera view.</summary>
        public int FullFrames;
        public int DetectedFull;
        /// <summary>Truth frames with some but not all joints in view.</summary>
        public int PartialFrames;
        public int DetectedPartial;
        /// <summary>Truth frames with no joint in view, and how many of them still had a detection.</summary>
        public int OutFrames;
        public int DetectedOut;

        /// <summary>Detection vs truth, 3D world angle error (elbow, knee: interior angle; shoulder: hip-shoulder-elbow).</summary>
        public ErrorStat Elbow, Knee, Shoulder;
        /// <summary>
        /// Same, front plane only (world x, y; depth z dropped). Information only: separates MediaPipe's depth
        /// estimate from its x, y placement. The mini character draws exactly this plane.
        /// </summary>
        public ErrorStat Elbow2D, Knee2D, Shoulder2D;
        /// <summary>Median depth error (detected z - truth z, meters, + = farther from the camera) of the shoulder
        /// center, wrist center and ankle center relative to the hip center.</summary>
        public float DepthShoulder = float.NaN, DepthWrist = float.NaN, DepthAnkle = float.NaN;
        /// <summary>Mini character (PoseSmoother) vs detection, 2D (x, y) angle error.</summary>
        public ErrorStat FigElbow, FigKnee, FigShoulder;
        /// <summary>Mini character vs truth, all six angles pooled (end to end, information only).</summary>
        public ErrorStat FigTruth;
        public int SwapFrames;

        /// <summary>Shoulder angle onset (+20 deg from rest) and half-way crossing; NaN when not measured.</summary>
        public float OnsetTruth = float.NaN, OnsetDetected = float.NaN, OnsetFigure = float.NaN;
        public float HalfTruth = float.NaN, HalfDetected = float.NaN, HalfFigure = float.NaN;

        /// <summary>Mean joint movement per detected frame (nose, shoulders, hips), body units (torso = 1).</summary>
        public float JitterTruth = float.NaN, JitterDetected = float.NaN, JitterFigure = float.NaN;
        public float JitterFigureMax = float.NaN;

        /// <summary>Walk-out clip: figure gone after the body left the view / back after it fully returned (negative = earlier).</summary>
        public float DisappearSec = float.NaN, ReappearSec = float.NaN;
        public bool Disappeared, Reappeared;
        /// <summary>Partial frames with both shoulders in view where the figure was shown (presence > 0.5).</summary>
        public float PartialShownRatio = float.NaN;

        public readonly List<string> Failures = new List<string>();

        public float DetectionRate => FullFrames == 0 ? float.NaN : DetectedFull / (float)FullFrames;
        public bool Pass => Failures.Count == 0;
        public float OnsetDelayDetected => OnsetDetected - OnsetTruth;
        public float OnsetDelayFigure => OnsetFigure - OnsetTruth;
        public float HalfDelayDetected => HalfDetected - HalfTruth;
        public float HalfDelayFigure => HalfFigure - HalfTruth;
    }

    /// <summary>
    /// Compares a rendered clip's truth with the MediaPipe detection (<c>pose_from_media.py</c>), and replays the
    /// detection through the R3 <see cref="PoseSmoother"/> at 60 fps like the mini character does.
    /// Pass thresholds are spec 56 4절 (draft); they are never relaxed to make a clip pass.
    /// </summary>
    public static class PoseCheck
    {
        public const float MinDetectionRate = 0.9f;
        public const float MaxAngleMedianDeg = 15f;
        public const float FigureDt = 1f / 60f;
        public const float OnsetDeg = 20f;
        /// <summary>Mini character panel: 300 px high, unitPerHeight 0.22 → pixels per torso length.</summary>
        public const float PanelPxPerUnit = 66f;

        private const int LS = (int)PoseJoint.LeftShoulder, RS = (int)PoseJoint.RightShoulder;
        private const int LE = (int)PoseJoint.LeftElbow, RE = (int)PoseJoint.RightElbow;
        private const int LW = (int)PoseJoint.LeftWrist, RW = (int)PoseJoint.RightWrist;
        private const int LH = (int)PoseJoint.LeftHip, RH = (int)PoseJoint.RightHip;
        private const int LK = (int)PoseJoint.LeftKnee, RK = (int)PoseJoint.RightKnee;
        private const int LA = (int)PoseJoint.LeftAnkle, RA = (int)PoseJoint.RightAnkle;
        private const int Nose = (int)PoseJoint.Nose;

        /// <summary>Angle triplets (a, vertex, c): elbows, knees, shoulders (left, right).</summary>
        private static readonly int[,] Angles =
        {
            { LS, LE, LW }, { RS, RE, RW },
            { LH, LK, LA }, { RH, RK, RA },
            { LH, LS, LE }, { RH, RS, RE },
        };

        private static readonly int[] LeftJoints = { LS, LE, LW, LH, LK, LA };
        private static readonly int[] RightJoints = { RS, RE, RW, RH, RK, RA };
        private static readonly int[] JitterJoints = { Nose, LS, RS, LH, RH };

        public sealed class Frame
        {
            public bool HasPerson;
            public readonly Vector2[] Screen = new Vector2[PoseProtocol.JointCount];
            public readonly Vector3[] World = new Vector3[PoseProtocol.JointCount];
            public readonly float[] Visibility = new float[PoseProtocol.JointCount];
            public int InView;
            public float TimeSec;
        }

        /// <summary>Called after every smoother tick: time, smoother, index of the last pushed detection (-1 = none).</summary>
        public delegate void TickProbe(float t, PoseSmoother smoother, int lastDetected);

        public static List<Frame> Frames(PoseRecording rec)
        {
            var list = new List<Frame>(rec.Count);
            var pf = new PoseFrame();
            for (int i = 0; i < rec.Count; i++)
            {
                rec.CopyTo(i, pf, (uint)(i + 1));
                var f = new Frame { HasPerson = pf.HasPerson, TimeSec = pf.TimestampMs / 1000f };
                for (int j = 0; j < PoseProtocol.JointCount; j++)
                {
                    f.Screen[j] = pf.Screen[j];
                    f.World[j] = pf.World[j];
                    f.Visibility[j] = pf.Visibility[j];
                    if (pf.HasPerson && pf.Visibility[j] >= 0.999f) f.InView++;
                }

                list.Add(f);
            }

            return list;
        }

        public static float Angle3(Frame f, int a, int b, int c) =>
            Vector3.Angle(f.World[a] - f.World[b], f.World[c] - f.World[b]);

        public static float Angle2(Vector2 a, Vector2 b, Vector2 c) => Vector2.Angle(a - b, c - b);

        private static Vector2 Xy(Vector3 v) => new Vector2(v.x, v.y);

        /// <summary>Signed: (detected - truth) depth of the l/r joint center relative to the hip center.</summary>
        private static float DepthError(Frame t, Frame d, int l, int r)
        {
            float tz = (t.World[l].z + t.World[r].z - t.World[LH].z - t.World[RH].z) * 0.5f;
            float dz = (d.World[l].z + d.World[r].z - d.World[LH].z - d.World[RH].z) * 0.5f;
            return dz - tz;
        }

        /// <summary>
        /// Labels swapped: matching each detected left/right joint pair to the other side's truth is closer than
        /// matching it to its own side (screen, aspect corrected), summed over the six pairs.
        /// </summary>
        public static bool IsSwapped(Frame truth, Frame det, float aspect = 4f / 3f)
        {
            float straight = 0f, cross = 0f;
            for (int k = 0; k < LeftJoints.Length; k++)
            {
                Vector2 tl = Scale(truth.Screen[LeftJoints[k]], aspect), tr = Scale(truth.Screen[RightJoints[k]], aspect);
                Vector2 dl = Scale(det.Screen[LeftJoints[k]], aspect), dr = Scale(det.Screen[RightJoints[k]], aspect);
                straight += Vector2.Distance(dl, tl) + Vector2.Distance(dr, tr);
                cross += Vector2.Distance(dl, tr) + Vector2.Distance(dr, tl);
            }

            return cross < straight;
        }

        private static Vector2 Scale(Vector2 v, float aspect) => new Vector2(v.x * aspect, v.y);

        public static ClipCheck Evaluate(PoseClip clip, PoseRecording truthRec, PoseRecording detectedRec, TickProbe probe = null)
        {
            if (truthRec.Count != detectedRec.Count)
                throw new ArgumentException($"{clip.Name}: truth has {truthRec.Count} frames, detection {detectedRec.Count}.");

            List<Frame> truth = Frames(truthRec);
            List<Frame> det = Frames(detectedRec);
            var r = new ClipCheck { Clip = clip.Name, Frames = truth.Count };

            var elbow = new List<float>();
            var knee = new List<float>();
            var shoulder = new List<float>();
            var elbow2 = new List<float>();
            var knee2 = new List<float>();
            var shoulder2 = new List<float>();
            var zShoulder = new List<float>();
            var zWrist = new List<float>();
            var zAnkle = new List<float>();
            for (int i = 0; i < truth.Count; i++)
            {
                Frame t = truth[i], d = det[i];
                bool full = t.HasPerson && t.InView == PoseProtocol.JointCount;
                if (!t.HasPerson || t.InView == 0)
                {
                    r.OutFrames++;
                    if (d.HasPerson) r.DetectedOut++;
                    continue;
                }

                if (!full)
                {
                    r.PartialFrames++;
                    if (d.HasPerson) r.DetectedPartial++;
                    continue;
                }

                r.FullFrames++;
                if (!d.HasPerson) continue;
                r.DetectedFull++;
                if (IsSwapped(t, d)) r.SwapFrames++;
                zShoulder.Add(DepthError(t, d, LS, RS));
                zWrist.Add(DepthError(t, d, LW, RW));
                zAnkle.Add(DepthError(t, d, LA, RA));
                for (int k = 0; k < Angles.GetLength(0); k++)
                {
                    float e = Mathf.Abs(Angle3(d, Angles[k, 0], Angles[k, 1], Angles[k, 2]) -
                                        Angle3(t, Angles[k, 0], Angles[k, 1], Angles[k, 2]));
                    (k < 2 ? elbow : k < 4 ? knee : shoulder).Add(e);
                    int a = Angles[k, 0], b = Angles[k, 1], c = Angles[k, 2];
                    float e2 = Mathf.Abs(Angle2(Xy(d.World[a]), Xy(d.World[b]), Xy(d.World[c])) -
                                         Angle2(Xy(t.World[a]), Xy(t.World[b]), Xy(t.World[c])));
                    (k < 2 ? elbow2 : k < 4 ? knee2 : shoulder2).Add(e2);
                }
            }

            r.Elbow = ErrorStat.From(elbow);
            r.Knee = ErrorStat.From(knee);
            r.Shoulder = ErrorStat.From(shoulder);
            r.Elbow2D = ErrorStat.From(elbow2);
            r.Knee2D = ErrorStat.From(knee2);
            r.Shoulder2D = ErrorStat.From(shoulder2);
            r.DepthShoulder = ErrorStat.From(zShoulder).Median;
            r.DepthWrist = ErrorStat.From(zWrist).Median;
            r.DepthAnkle = ErrorStat.From(zAnkle).Median;

            ReplayFigure(clip, truth, det, r, probe);
            Latency(clip, truth, det, r);
            Jitter(truth, det, r);
            Judge(clip, r);
            return r;
        }

        private static void ReplayFigure(PoseClip clip, List<Frame> truth, List<Frame> det, ClipCheck r, TickProbe probe)
        {
            var s = new PoseSmoother();
            var frame = new PoseFrame();
            var figElbow = new List<float>();
            var figKnee = new List<float>();
            var figShoulder = new List<float>();
            var figTruth = new List<float>();
            var figShoulderSeries = new List<Vector2>(); // (t, angle) of the timed arm
            var jitter = new List<float>();
            Vector2[] lastJitter = new Vector2[JitterJoints.Length];
            bool hasLastJitter = false;

            float tGone = float.NaN, tBack = float.NaN;
            if (clip.LeavesFrame) FindWalkTimes(truth, out tGone, out tBack);
            int partialShoulders = 0, partialShown = 0;

            int next = 0, last = -1;
            float end = det.Count == 0 ? 0f : det[det.Count - 1].TimeSec + 1f;
            int timedShoulder = clip.Arm == ActiveArm.Right ? 5 : 4;
            for (float t = 0f; t < end; t += FigureDt)
            {
                bool pushed = false;
                while (next < det.Count && det[next].TimeSec <= t + 1e-4f)
                {
                    Fill(frame, det[next], (uint)(next + 1));
                    s.Push(frame, t);
                    last = next;
                    next++;
                    pushed = true;
                }

                s.Tick(t, FigureDt);
                probe?.Invoke(t, s, last);

                if (clip.LeavesFrame)
                {
                    if (!float.IsNaN(tGone) && !r.Disappeared && t >= tGone && s.Presence <= 0f && (float.IsNaN(tBack) || t < tBack))
                    {
                        r.Disappeared = true;
                        r.DisappearSec = t - tGone;
                    }

                    if (!float.IsNaN(tBack) && r.Disappeared && !r.Reappeared && t >= tGone && s.Presence >= 0.99f)
                    {
                        r.Reappeared = true;
                        r.ReappearSec = t - tBack;
                    }
                }

                if (last < 0) continue;
                Frame d = det[last], tr = truth[last];
                bool shown = s.Settled && s.Presence >= 0.99f;

                if (pushed && tr.HasPerson && tr.InView > 0 && tr.InView < PoseProtocol.JointCount &&
                    tr.Visibility[LS] >= 0.999f && tr.Visibility[RS] >= 0.999f)
                {
                    partialShoulders++;
                    if (s.Settled && s.Presence > 0.5f) partialShown++;
                }

                if (!d.HasPerson || !shown) continue;

                for (int k = 0; k < Angles.GetLength(0); k++)
                {
                    int a = Angles[k, 0], b = Angles[k, 1], c = Angles[k, 2];
                    if (s.JointAlpha(a) < 1f || s.JointAlpha(b) < 1f || s.JointAlpha(c) < 1f) continue;
                    if (d.Visibility[a] < PoseSmoother.VisibleThreshold || d.Visibility[b] < PoseSmoother.VisibleThreshold ||
                        d.Visibility[c] < PoseSmoother.VisibleThreshold) continue;
                    float fig = Angle2(s.Joint(a), s.Joint(b), s.Joint(c));
                    float e = Mathf.Abs(fig - Angle2(Xy(d.World[a]), Xy(d.World[b]), Xy(d.World[c])));
                    (k < 2 ? figElbow : k < 4 ? figKnee : figShoulder).Add(e);
                    if (tr.InView == PoseProtocol.JointCount)
                        figTruth.Add(Mathf.Abs(fig - Angle2(Xy(tr.World[a]), Xy(tr.World[b]), Xy(tr.World[c]))));
                    if (k == timedShoulder) figShoulderSeries.Add(new Vector2(t, fig));
                }

                if (pushed)
                {
                    bool all = true;
                    for (int k = 0; k < JitterJoints.Length; k++)
                        if (s.JointAlpha(JitterJoints[k]) < 1f) all = false;
                    if (all)
                    {
                        if (hasLastJitter)
                        {
                            float sum = 0f;
                            for (int k = 0; k < JitterJoints.Length; k++)
                                sum += Vector2.Distance(s.Joint(JitterJoints[k]), lastJitter[k]);
                            jitter.Add(sum / JitterJoints.Length);
                        }

                        for (int k = 0; k < JitterJoints.Length; k++) lastJitter[k] = s.Joint(JitterJoints[k]);
                        hasLastJitter = true;
                    }
                    else hasLastJitter = false;
                }
            }

            r.FigElbow = ErrorStat.From(figElbow);
            r.FigKnee = ErrorStat.From(figKnee);
            r.FigShoulder = ErrorStat.From(figShoulder);
            r.FigTruth = ErrorStat.From(figTruth);
            if (partialShoulders > 0) r.PartialShownRatio = partialShown / (float)partialShoulders;
            if (jitter.Count > 0)
            {
                float sum = 0f, max = 0f;
                foreach (float j in jitter)
                {
                    sum += j;
                    max = Mathf.Max(max, j);
                }

                r.JitterFigure = sum / jitter.Count;
                r.JitterFigureMax = max;
            }

            if (clip.Arm != ActiveArm.None && truth.Count > 0)
            {
                float rest = figShoulderSeries.Count > 0 ? figShoulderSeries[0].y : float.NaN;
                float half = HalfLevel(truth, timedShoulder);
                foreach (Vector2 p in figShoulderSeries)
                {
                    if (float.IsNaN(r.OnsetFigure) && p.y >= rest + OnsetDeg) r.OnsetFigure = p.x;
                    if (float.IsNaN(r.HalfFigure) && p.y >= half) r.HalfFigure = p.x;
                }
            }
        }

        private static void FindWalkTimes(List<Frame> truth, out float tGone, out float tBack)
        {
            tGone = tBack = float.NaN;
            int i = 0;
            while (i < truth.Count && truth[i].InView > 0) i++;
            if (i >= truth.Count) return;
            tGone = truth[i].TimeSec;
            while (i < truth.Count && truth[i].InView < PoseProtocol.JointCount) i++;
            if (i < truth.Count) tBack = truth[i].TimeSec;
        }

        private static float HalfLevel(List<Frame> truth, int k)
        {
            float rest = Angle3(truth[0], Angles[k, 0], Angles[k, 1], Angles[k, 2]);
            float peak = rest;
            foreach (Frame f in truth)
                if (f.HasPerson) peak = Mathf.Max(peak, Angle3(f, Angles[k, 0], Angles[k, 1], Angles[k, 2]));
            return (rest + peak) * 0.5f;
        }

        private static void Latency(PoseClip clip, List<Frame> truth, List<Frame> det, ClipCheck r)
        {
            if (clip.Arm == ActiveArm.None || truth.Count == 0) return;
            int k = clip.Arm == ActiveArm.Right ? 5 : 4;
            float half = HalfLevel(truth, k);
            float restT = Angle3(truth[0], Angles[k, 0], Angles[k, 1], Angles[k, 2]);
            float restD = float.NaN;
            for (int i = 0; i < truth.Count; i++)
            {
                float at = Angle3(truth[i], Angles[k, 0], Angles[k, 1], Angles[k, 2]);
                if (float.IsNaN(r.OnsetTruth) && at >= restT + OnsetDeg) r.OnsetTruth = truth[i].TimeSec;
                if (float.IsNaN(r.HalfTruth) && at >= half) r.HalfTruth = truth[i].TimeSec;
                if (!det[i].HasPerson) continue;
                float ad = Angle3(det[i], Angles[k, 0], Angles[k, 1], Angles[k, 2]);
                if (float.IsNaN(restD)) restD = ad;
                if (float.IsNaN(r.OnsetDetected) && ad >= restD + OnsetDeg) r.OnsetDetected = det[i].TimeSec;
                if (float.IsNaN(r.HalfDetected) && ad >= half) r.HalfDetected = det[i].TimeSec;
            }
        }

        private static void Jitter(List<Frame> truth, List<Frame> det, ClipCheck r)
        {
            r.JitterTruth = MeanMove(truth);
            r.JitterDetected = MeanMove(det);
        }

        /// <summary>Mean per-frame movement of nose, shoulders, hips in world (x, y) / torso length.</summary>
        private static float MeanMove(List<Frame> frames)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                Frame a = frames[i - 1], b = frames[i];
                if (!a.HasPerson || !b.HasPerson) continue;
                float torso = Torso(b);
                if (torso < 1e-3f) continue;
                float move = 0f;
                foreach (int j in JitterJoints) move += Vector2.Distance(Xy(a.World[j]), Xy(b.World[j]));
                sum += move / JitterJoints.Length / torso;
                n++;
            }

            return n == 0 ? float.NaN : sum / n;
        }

        private static float Torso(Frame f)
        {
            Vector2 sh = (Xy(f.World[LS]) + Xy(f.World[RS])) * 0.5f;
            Vector2 hp = (Xy(f.World[LH]) + Xy(f.World[RH])) * 0.5f;
            return Vector2.Distance(sh, hp);
        }

        private static void Fill(PoseFrame dst, Frame src, uint seq)
        {
            dst.HasPerson = src.HasPerson;
            dst.Seq = seq;
            dst.TimestampMs = (uint)Mathf.RoundToInt(src.TimeSec * 1000f);
            for (int j = 0; j < PoseProtocol.JointCount; j++)
            {
                dst.Screen[j] = src.Screen[j];
                dst.World[j] = src.World[j];
                dst.Visibility[j] = src.Visibility[j];
            }
        }

        private static void Judge(PoseClip clip, ClipCheck r)
        {
            if (r.FullFrames > 0 && r.DetectionRate < MinDetectionRate)
                r.Failures.Add($"검출률 {r.DetectionRate:P0} < {MinDetectionRate:P0}");
            Check(r, "팔꿈치 각도", r.Elbow);
            Check(r, "무릎 각도", r.Knee);
            Check(r, "어깨 각도", r.Shoulder);
            if (r.SwapFrames > 0) r.Failures.Add($"좌우 뒤바뀜 {r.SwapFrames}프레임");
            Check(r, "인형 팔꿈치", r.FigElbow);
            Check(r, "인형 무릎", r.FigKnee);
            Check(r, "인형 어깨", r.FigShoulder);
            if (clip.LeavesFrame)
            {
                if (!r.Disappeared) r.Failures.Add("화면 밖에서 인형이 사라지지 않음");
                if (!r.Reappeared) r.Failures.Add("돌아와도 인형이 나타나지 않음");
            }
        }

        private static void Check(ClipCheck r, string label, ErrorStat s)
        {
            if (s.Count > 0 && s.Median > MaxAngleMedianDeg)
                r.Failures.Add($"{label} 오차 중앙값 {s.Median:F1}° > {MaxAngleMedianDeg:F0}°");
        }
    }
}
