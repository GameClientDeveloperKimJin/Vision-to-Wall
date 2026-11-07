using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Pose recording CSV written by Tools/HandSensor (<c>--preview</c>, Enter) or any tool using the same format:
    /// header <c>t_ms,count,{Joint}_sx,_sy,_wx,_wy,_wz,_v</c> for the 13 joints in <see cref="PoseJoint"/> order;
    /// count 0 rows are just <c>t_ms,0</c>. Used to replay real MediaPipe poses without a person in front of
    /// the camera (tests, Dev scene). Parsing allocates; do it once at load.
    /// </summary>
    public sealed class PoseRecording
    {
        public readonly struct Sample
        {
            public readonly uint TimestampMs;
            public readonly bool HasPerson;
            /// <summary>Index into the joint arrays, or -1 when <see cref="HasPerson"/> is false.</summary>
            public readonly int Offset;

            public Sample(uint timestampMs, bool hasPerson, int offset)
            {
                TimestampMs = timestampMs;
                HasPerson = hasPerson;
                Offset = offset;
            }
        }

        private const int Columns = 2 + PoseProtocol.JointCount * PoseProtocol.FloatsPerJoint;

        private readonly List<Sample> _samples = new List<Sample>();
        private readonly List<Vector2> _screen = new List<Vector2>();
        private readonly List<Vector3> _world = new List<Vector3>();
        private readonly List<float> _visibility = new List<float>();

        public int Count => _samples.Count;
        public Sample this[int index] => _samples[index];
        /// <summary>Last timestamp, in seconds.</summary>
        public float DurationSec => _samples.Count == 0 ? 0f : _samples[_samples.Count - 1].TimestampMs / 1000f;

        /// <summary>Throws <see cref="FormatException"/> on a bad header or row.</summary>
        public static PoseRecording Parse(string csv)
        {
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            var rec = new PoseRecording();
            string[] lines = csv.Split('\n');
            if (lines.Length == 0 || !lines[0].StartsWith("t_ms,count", StringComparison.Ordinal))
                throw new FormatException("Not a pose recording (header must start with t_ms,count).");

            for (int li = 1; li < lines.Length; li++)
            {
                string line = lines[li].Trim();
                if (line.Length == 0) continue;
                string[] f = line.Split(',');
                if (f.Length < 2) throw new FormatException($"Line {li + 1}: too few columns.");

                uint t = uint.Parse(f[0], CultureInfo.InvariantCulture);
                int count = int.Parse(f[1], CultureInfo.InvariantCulture);
                if (count == 0)
                {
                    rec._samples.Add(new Sample(t, false, -1));
                    continue;
                }

                if (count != PoseProtocol.JointCount || f.Length < Columns)
                    throw new FormatException($"Line {li + 1}: expected {PoseProtocol.JointCount} joints.");

                int offset = rec._screen.Count;
                for (int j = 0; j < PoseProtocol.JointCount; j++)
                {
                    int c = 2 + j * PoseProtocol.FloatsPerJoint;
                    rec._screen.Add(new Vector2(F(f[c]), F(f[c + 1])));
                    rec._world.Add(new Vector3(F(f[c + 2]), F(f[c + 3]), F(f[c + 4])));
                    rec._visibility.Add(F(f[c + 5]));
                }

                rec._samples.Add(new Sample(t, true, offset));
            }

            return rec;
        }

        /// <summary>Fills <paramref name="dst"/> with sample <paramref name="index"/>. No allocation.</summary>
        public void CopyTo(int index, PoseFrame dst, uint seq)
        {
            Sample s = _samples[index];
            if (!s.HasPerson)
            {
                dst.Clear();
            }
            else
            {
                for (int j = 0; j < PoseProtocol.JointCount; j++)
                {
                    dst.Screen[j] = _screen[s.Offset + j];
                    dst.World[j] = _world[s.Offset + j];
                    dst.Visibility[j] = _visibility[s.Offset + j];
                }

                dst.HasPerson = true;
            }

            dst.Seq = seq;
            dst.TimestampMs = s.TimestampMs;
        }

        private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
