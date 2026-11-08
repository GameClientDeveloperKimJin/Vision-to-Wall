using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using VisionToWall.Core;
using VisionToWall.UI;

namespace VisionToWall.PoseTest
{
    /// <summary>
    /// Ground-truth CSV of a rendered clip (<c>&lt;clip&gt;_truth.csv</c>). The first columns are exactly the pose
    /// recording format (<see cref="PoseRecording"/>: <c>t_ms,count,{Joint}_sx,_sy,_wx,_wy,_wz,_v</c> x 13), so the
    /// same file replays through anything that reads sensor recordings. Then, on every row:
    /// <list type="bullet">
    /// <item><c>Hips_px,Hips_py,Hips_pz</c>: hips bone in scene meters (camera at the origin, looking +z).</item>
    /// <item><c>{Bone}_qx,_qy,_qz,_qw</c> for each <see cref="PoseRig.TruthBones"/>: rotation from the T-pose in
    /// the avatar root frame (avatar facing +z, its left at -x): <c>bone.rotation = root.rotation * q * bind</c>,
    /// where <c>bind</c> is the bone's T-pose rotation relative to the root.</item>
    /// </list>
    /// Rows with count 0 (no joint in view) still carry the hips and bone columns after 78 empty joint cells.
    /// </summary>
    public sealed class PoseTruth
    {
        public const int JointColumns = 2 + PoseProtocol.JointCount * PoseProtocol.FloatsPerJoint;

        public readonly PoseRecording Joints;
        private readonly List<Vector3> _hips = new List<Vector3>();
        private readonly List<Quaternion> _bones = new List<Quaternion>();

        private PoseTruth(PoseRecording joints) => Joints = joints;

        public int Count => Joints.Count;

        public Vector3 HipsPosition(int frame) => _hips[frame];

        public Quaternion BoneDelta(int frame, HumanBodyBones bone)
        {
            int i = Array.IndexOf(PoseRig.TruthBones, bone);
            if (i < 0) throw new ArgumentException("Bone not in the truth CSV: " + bone, nameof(bone));
            return _bones[frame * PoseRig.TruthBones.Length + i];
        }

        public static string Header()
        {
            var sb = new StringBuilder("t_ms,count");
            string[] fields = { "sx", "sy", "wx", "wy", "wz", "v" };
            foreach (string name in Enum.GetNames(typeof(PoseJoint)))
                foreach (string f in fields)
                    sb.Append(',').Append(name).Append('_').Append(f);
            sb.Append(",Hips_px,Hips_py,Hips_pz");
            foreach (HumanBodyBones b in PoseRig.TruthBones)
                sb.Append(',').Append(b).Append("_qx,").Append(b).Append("_qy,").Append(b).Append("_qz,").Append(b).Append("_qw");
            return sb.ToString();
        }

        /// <summary>One CSV line. <paramref name="joints"/>: 13 x 6 floats, ignored when count is 0.</summary>
        public static void AppendRow(StringBuilder sb, int tMs, int count, float[] joints, Vector3 hips, Quaternion[] bones)
        {
            sb.Append(tMs.ToString(CultureInfo.InvariantCulture)).Append(',').Append(count);
            int n = PoseProtocol.JointCount * PoseProtocol.FloatsPerJoint;
            for (int i = 0; i < n; i++)
            {
                sb.Append(',');
                if (count > 0) sb.Append(joints[i].ToString("F4", CultureInfo.InvariantCulture));
            }

            sb.Append(',').Append(F(hips.x)).Append(',').Append(F(hips.y)).Append(',').Append(F(hips.z));
            foreach (Quaternion q in bones)
                sb.Append(',').Append(F(q.x)).Append(',').Append(F(q.y)).Append(',').Append(F(q.z)).Append(',').Append(F(q.w));
            sb.Append('\n');
        }

        /// <summary>Throws <see cref="FormatException"/> when the header or a row does not match.</summary>
        public static PoseTruth Parse(string csv)
        {
            // Count 0 rows keep 78 empty joint cells; PoseRecording expects "t_ms,0" there, so strip them for it.
            string[] lines = csv.Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header())
                throw new FormatException("Not a pose truth CSV (header mismatch).");

            var jointCsv = new StringBuilder(csv.Length);
            jointCsv.Append(lines[0]).Append('\n');
            var parsed = new List<string[]>();
            for (int li = 1; li < lines.Length; li++)
            {
                string line = lines[li].Trim();
                if (line.Length == 0) continue;
                string[] f = line.Split(',');
                int expected = JointColumns + 3 + PoseRig.TruthBones.Length * 4;
                if (f.Length != expected) throw new FormatException($"Line {li + 1}: {f.Length} columns, expected {expected}.");
                parsed.Add(f);
                if (f[1] == "0") jointCsv.Append(f[0]).Append(",0\n");
                else jointCsv.Append(line).Append('\n');
            }

            var truth = new PoseTruth(PoseRecording.Parse(jointCsv.ToString()));
            foreach (string[] f in parsed)
            {
                int c = JointColumns;
                truth._hips.Add(new Vector3(P(f[c]), P(f[c + 1]), P(f[c + 2])));
                c += 3;
                for (int b = 0; b < PoseRig.TruthBones.Length; b++, c += 4)
                    truth._bones.Add(new Quaternion(P(f[c]), P(f[c + 1]), P(f[c + 2]), P(f[c + 3])));
            }

            return truth;
        }

        private static string F(float v) => v.ToString("F5", CultureInfo.InvariantCulture);
        private static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
