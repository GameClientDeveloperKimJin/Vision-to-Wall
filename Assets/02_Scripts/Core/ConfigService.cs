using System;
using System.IO;
using UnityEngine;

namespace VisionToWall.Core
{
    /// <summary>
    /// Loads and saves StreamingAssets/config.json and holds the shared runtime instance.
    /// Modules read <see cref="Current"/>; the engineer panel edits it and calls <see cref="NotifyChanged"/>.
    /// </summary>
    public static class ConfigService
    {
        public const string FileName = "config.json";

        private static AppConfig _current;

        /// <summary>Shared runtime config. Falls back to defaults if <see cref="Load"/> was never called (dev scenes).</summary>
        public static AppConfig Current => _current ??= new AppConfig();

        /// <summary>Raised on the main thread after a runtime value change.</summary>
        public static event Action<AppConfig> Changed;

        public static string FilePath => Path.Combine(Application.streamingAssetsPath, FileName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports Enter Play Mode without domain reload.
            _current = null;
            Changed = null;
        }

        /// <summary>
        /// Loads config.json. Missing file: writes defaults. Parse error: logs a warning and uses defaults.
        /// </summary>
        public static AppConfig Load()
        {
            var config = new AppConfig();
            string path = FilePath;

            try
            {
                if (File.Exists(path))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
                }
                else
                {
                    Debug.LogWarning($"[Config] {path} not found. Writing defaults.");
                    WriteFile(path, config);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Config] Failed to load {path}: {e.Message}. Using defaults.");
                config = new AppConfig();
            }

            Sanitize(config);
            _current = config;
            Debug.Log($"[Config] Loaded: camera={config.cameraIndex} grid={config.gridWidth}x{config.gridHeight} " +
                      $"threshold={config.threshold} udp={config.udpIp}:{config.udpPort}");
            return config;
        }

        /// <summary>Writes <see cref="Current"/> to config.json (pretty printed).</summary>
        public static bool Save()
        {
            try
            {
                Sanitize(Current);
                WriteFile(FilePath, Current);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Config] Failed to save {FilePath}: {e.Message}");
                return false;
            }
        }

        public static void NotifyChanged()
        {
            Sanitize(Current);
            Changed?.Invoke(Current);
        }

        private static void WriteFile(string path, AppConfig config)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(config, true));
        }

        private static void Sanitize(AppConfig c)
        {
            c.sensorMode = string.IsNullOrWhiteSpace(c.sensorMode) ? AppConfig.SensorModeWebcam : c.sensorMode.Trim().ToLowerInvariant();
            if (c.sensorMode != AppConfig.SensorModeExternal) c.sensorMode = AppConfig.SensorModeWebcam;
            c.cameraIndex = Mathf.Max(0, c.cameraIndex);
            c.cameraWidth = Mathf.Clamp(c.cameraWidth, 160, 3840);
            c.cameraHeight = Mathf.Clamp(c.cameraHeight, 120, 2160);
            c.cameraFps = Mathf.Clamp(c.cameraFps, 1, 240);
            c.gridWidth = Mathf.Clamp(c.gridWidth, 1, 64);
            c.gridHeight = Mathf.Clamp(c.gridHeight, 1, 64);
            if (c.gridWidth * c.gridHeight > MotionProtocol.MaxPoints)
                c.gridHeight = MotionProtocol.MaxPoints / c.gridWidth;
            c.threshold = Mathf.Clamp(c.threshold, 0.001f, 1f);
            if (string.IsNullOrWhiteSpace(c.udpIp)) c.udpIp = "127.0.0.1";
            c.udpPort = Mathf.Clamp(c.udpPort, 1, 65535);
            c.idleTimeoutSec = Mathf.Max(0.5f, c.idleTimeoutSec);
        }
    }
}
