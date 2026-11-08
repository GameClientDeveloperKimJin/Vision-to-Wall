namespace VisionToWall.Core
{
    /// <summary>Mirrors StreamingAssets/config.json. Field names are the JSON keys.</summary>
    [System.Serializable]
    public class AppConfig
    {
        public const string SensorModeWebcam = "webcam";
        public const string SensorModeExternal = "external";

        // Sensor source (restart required)
        // "webcam":   Unity frame-diff sensor opens the webcam and sends UDP itself.
        // "external": another process (e.g. Tools/HandSensor) owns the webcam and sends UDP; Unity only receives.
        public string sensorMode = SensorModeWebcam;

        public bool UseExternalSensor => sensorMode == SensorModeExternal;

        // Camera (restart required)
        public int cameraIndex = 0;
        public int cameraWidth = 640;   // C270: 1280x720 drops to ~8fps (USB2 uncompressed)
        public int cameraHeight = 480;
        public int cameraFps = 60;

        // Camera (runtime)
        public bool mirror = true;

        // Sensor
        public int gridWidth = 16;      // restart required
        public int gridHeight = 16;     // restart required
        public float threshold = 0.15f; // runtime, luminance delta 0..1

        // Hand sensor (sensorMode = "external", runtime: Tools/HandSensor re-reads config.json within ~0.5s)
        public float handSpeedRef = 1.5f;   // hand speed (screen widths/s) that maps to intensity v = 1. Lower = more reactive
        public float handMinSpeed = 0.05f;  // hands slower than this are not sent (still hand ignored)
        public bool handEmitStill = false;  // true: still hands are sent with v = 0.1 (position marker)

        // Network (restart required)
        public string udpIp = "127.0.0.1";
        public int udpPort = 9000;
        public int udpBodyPort = 9001;  // body position stream from Tools/HandSensor
        public int udpPosePort = 9002;  // pose joint stream from Tools/HandSensor (0 = off)

        // Exhibition (runtime)
        public float idleTimeoutSec = 5f;

        // Game flow
        public float awakenChargeSec = 3f;      // continuous motion needed to awaken the boss
        public float awakenMinMotion = 0.15f;   // motion at or above this charges the gauge
        public float awakenGraceSec = 0.5f;     // charge survives a gap this long (hand reversal)
        public float awakenTransitionSec = 2f;
        public float combatDurationSec = 60f;
        public float combatAbandonSec = 10f;    // no input this long -> game over (kiosk reset)
        public float resultDurationSec = 5f;
        public float rearmCooldownSec = 5f;     // no charging right after returning to the wall

        // Combat
        public float aimSec = 1.5f;
        public float aimLockSec = 0.3f;         // final part of aimSec where the aim stops following
        public float projectileFlightSec = 0.5f;
        public float attackRecoverSec = 1.5f;
        public float hitRadius = 0.15f;         // in screen heights, aspect corrected
        public float shieldThreshold = 0.8f;    // motion needed to raise the shield
        public float shieldDurationSec = 1.5f;
        public float shieldCooldownSec = 1.5f;
        public bool shieldPoseEnabled = true;   // raising an arm also raises the shield (pose stream only)
        public int bossMaxHp = 5;               // parries needed to win
        public int playerMaxHp = 5;             // hits allowed

        // Device control simulation (v2.1, restart required). Off = no packets, no virtual light.
        public bool deviceEnabled = false;
        public int deviceId = 1;                // packet id byte
        public int deviceSendHz = 30;
        public bool deviceLogPackets = false;   // hex log, at most one line per second
    }
}
