using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.App
{
    /// <summary>
    /// Composition root. The only place where modules are wired together.
    /// Phase 0: loads config only. Phase 2: initializes Network -> Sensor -> Render -> UI.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class AppBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            AppConfig config = ConfigService.Load();

            // Phase 2 wiring (receiver first so the port is open before the first packet):
            // receiver.Initialize(config);
            // sender.Initialize(config);
            // sensor.Initialize(config, sender);
            // fsm.Initialize(config, receiver);
            // wall.Initialize(receiver, fsm);
            // panel.Initialize(sensor, receiver, receiver, fsm);
        }
    }
}
