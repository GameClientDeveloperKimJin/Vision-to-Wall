using System;
using System.IO;
using UnityEngine;

namespace VisionToWall.Weather
{
    public enum SecretsStatus { Ok, Missing, Unreadable, Empty, Placeholder }

    /// <summary>
    /// Reads the KMA key from StreamingAssets/secrets.json (git ignored, template secrets.example.json).
    /// Never logs or returns the key in any message.
    /// </summary>
    public static class WeatherSecrets
    {
        public const string FileName = "secrets.json";

        [Serializable] private sealed class SecretsFile { public string kmaServiceKey; }

        public static string DefaultPath => Path.Combine(Application.streamingAssetsPath, FileName);

        public static SecretsStatus TryLoadKmaKey(out string key) => TryLoadKmaKey(DefaultPath, out key);

        public static SecretsStatus TryLoadKmaKey(string path, out string key)
        {
            key = null;
            string text;
            try
            {
                if (!File.Exists(path)) return SecretsStatus.Missing;
                text = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return SecretsStatus.Unreadable;
            }
            return TryParse(text, out key);
        }

        /// <summary>Public for tests.</summary>
        public static SecretsStatus TryParse(string json, out string key)
        {
            key = null;
            SecretsFile file;
            try
            {
                file = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<SecretsFile>(json);
            }
            catch (Exception)
            {
                return SecretsStatus.Unreadable;
            }

            string value = file?.kmaServiceKey?.Trim();
            if (string.IsNullOrEmpty(value)) return SecretsStatus.Empty;
            if (!LooksLikeKey(value)) return SecretsStatus.Placeholder;
            key = value;
            return SecretsStatus.Ok;
        }

        /// <summary>Keys are printable ASCII without spaces; the template's Korean instruction text is not.</summary>
        private static bool LooksLikeKey(string value)
        {
            if (value.Length < 16) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c <= ' ' || c > '~') return false;
            }
            return true;
        }
    }
}
