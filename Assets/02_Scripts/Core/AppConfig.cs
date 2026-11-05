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

        // Network (restart required)
        public string udpIp = "127.0.0.1";
        public int udpPort = 9000;

        // Exhibition (runtime)
        public float idleTimeoutSec = 5f;
    }
}
