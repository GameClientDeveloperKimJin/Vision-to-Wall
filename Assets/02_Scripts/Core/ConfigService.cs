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
            c.handSpeedRef = Mathf.Clamp(c.handSpeedRef, 0.05f, 20f);
            c.handMinSpeed = Mathf.Clamp(c.handMinSpeed, 0f, 5f);
            if (string.IsNullOrWhiteSpace(c.udpIp)) c.udpIp = "127.0.0.1";
            c.udpPort = Mathf.Clamp(c.udpPort, 1, 65535);
            c.idleTimeoutSec = Mathf.Max(0.5f, c.idleTimeoutSec);

            c.udpBodyPort = Mathf.Clamp(c.udpBodyPort, 1, 65535);
            if (c.udpBodyPort == c.udpPort)
            {
                Debug.LogWarning($"[Config] udpBodyPort must differ from udpPort ({c.udpPort}). Using {c.udpPort + 1}.");
                c.udpBodyPort = c.udpPort < 65535 ? c.udpPort + 1 : c.udpPort - 1;
            }

            c.udpPosePort = Mathf.Clamp(c.udpPosePort, 0, 65535);
            if (c.udpPosePort != 0 && (c.udpPosePort == c.udpPort || c.udpPosePort == c.udpBodyPort))
            {
                Debug.LogWarning($"[Config] udpPosePort must differ from udpPort and udpBodyPort. Pose stream off.");
                c.udpPosePort = 0;
            }

            c.awakenChargeSec = Mathf.Clamp(c.awakenChargeSec, 0.5f, 30f);
            c.awakenMinMotion = Mathf.Clamp01(c.awakenMinMotion);
            c.awakenGraceSec = Mathf.Clamp(c.awakenGraceSec, 0f, 5f);
            c.awakenTransitionSec = Mathf.Clamp(c.awakenTransitionSec, 0.1f, 30f);
            c.combatDurationSec = Mathf.Clamp(c.combatDurationSec, 5f, 600f);
            c.combatAbandonSec = Mathf.Clamp(c.combatAbandonSec, 1f, 600f);
            c.resultDurationSec = Mathf.Clamp(c.resultDurationSec, 0.1f, 60f);
            c.rearmCooldownSec = Mathf.Clamp(c.rearmCooldownSec, 0f, 60f);

            c.aimSec = Mathf.Clamp(c.aimSec, 0.2f, 10f);
            c.aimLockSec = Mathf.Clamp(c.aimLockSec, 0f, c.aimSec * 0.9f); // keeps aimLockSec below aimSec
            c.projectileFlightSec = Mathf.Clamp(c.projectileFlightSec, 0.05f, 5f);
            c.attackRecoverSec = Mathf.Clamp(c.attackRecoverSec, 0f, 10f);
            c.hitRadius = Mathf.Clamp(c.hitRadius, 0.01f, 1f);
            c.shieldThreshold = Mathf.Clamp(c.shieldThreshold, 0.05f, 1f);
            c.shieldDurationSec = Mathf.Clamp(c.shieldDurationSec, 0.1f, 10f);
            c.shieldCooldownSec = Mathf.Clamp(c.shieldCooldownSec, 0f, 30f);
            c.bossMaxHp = Mathf.Clamp(c.bossMaxHp, 1, 100);
            c.playerMaxHp = Mathf.Clamp(c.playerMaxHp, 1, 100);

            c.deviceId = Mathf.Clamp(c.deviceId, 0, 255);
            c.deviceSendHz = Mathf.Clamp(c.deviceSendHz, 1, 60);

            c.weatherProvider = string.IsNullOrWhiteSpace(c.weatherProvider) ? AppConfig.WeatherProviderKma : c.weatherProvider.Trim().ToLowerInvariant();
            if (c.weatherLocationName == null) c.weatherLocationName = "";
            c.weatherGridX = Mathf.Clamp(c.weatherGridX, 1, 149);   // KMA grid range
            c.weatherGridY = Mathf.Clamp(c.weatherGridY, 1, 253);
            c.weatherRefreshMin = Mathf.Clamp(c.weatherRefreshMin, 10, 180); // the forecast updates hourly
            c.weatherOverride = string.IsNullOrWhiteSpace(c.weatherOverride) ? "" : c.weatherOverride.Trim().ToLowerInvariant();
        }
    }
}
