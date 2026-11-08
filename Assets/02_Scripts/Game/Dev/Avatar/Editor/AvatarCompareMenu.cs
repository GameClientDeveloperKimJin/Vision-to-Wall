using System.IO;
using UnityEditor;
using UnityEngine;
using VisionToWall.UI;

namespace VisionToWall.Game.Dev
{
    /// <summary>Menu for <see cref="AvatarCompare"/>: newest <c>Tools/HandSensor/recordings/compare_*</c> -> avatar.csv.</summary>
    public static class AvatarCompareMenu
    {
        public static string RecordingsDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "HandSensor", "recordings"));

        [MenuItem("VisionToWall/Avatar/Compare Latest Recording")]
        public static void CompareLatest()
        {
            string dir = Latest();
            if (dir == null)
            {
                Debug.LogWarning("[R4b] no recordings/compare_* folder (run Tools/HandSensor/pose_compare_record.py)");
                return;
            }

            Debug.Log("[R4b] " + Compare(dir, "avatar.csv", new AvatarPoseSolver()));
        }

        public static string Latest()
        {
            if (!Directory.Exists(RecordingsDir)) return null;
            string[] dirs = Directory.GetDirectories(RecordingsDir, "compare_*");
            if (dirs.Length == 0) return null;
            System.Array.Sort(dirs);
            return dirs[dirs.Length - 1];
        }

        /// <summary>Replays <c>joints.csv</c> in <paramref name="dir"/>, writes <paramref name="outName"/>, returns a summary line.</summary>
        public static string Compare(string dir, string outName, AvatarPoseSolver solver)
        {
            PoseRecording rec = PoseRecording.Parse(File.ReadAllText(Path.Combine(dir, "joints.csv")));
            using (var rig = new AvatarCompare.Rig())
            {
                var frames = AvatarCompare.Run(rec, solver, rig);
                File.WriteAllText(Path.Combine(dir, outName), AvatarCompare.ToCsv(frames));
                var s = AvatarCompare.Summary(frames);
                return $"{Path.GetFileName(dir)}/{outName}: {frames.Count} packets, angle median {s.median:F1} p95 {s.p95:F1} over25 {s.over25:P1}, depth (avatar frontal - camera ratio) median {s.depthMedian:F2}, visible limbs at the image edge {s.offFrame:P1}";
            }
        }
    }
}
