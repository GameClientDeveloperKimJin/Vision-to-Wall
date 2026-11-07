using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Device.Tests
{
    public class DeviceControllerTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void Disabled_ControllerAndLightAreOff()
        {
            _root = new GameObject("DeviceTest");
            var controller = _root.AddComponent<DeviceController>();
            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(_root.transform);
            var light = lightObject.AddComponent<VirtualMovingLight>();
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(_root.transform);
            var camera = cameraObject.AddComponent<Camera>();

            controller.Initialize(new AppConfig(), null, new LogPacketSink(false)); // deviceEnabled defaults to false
            light.Initialize(controller, camera);

            Assert.IsFalse(controller.enabled);
            Assert.IsFalse(controller.IsActive);
            Assert.IsFalse(lightObject.activeSelf);
            Assert.AreEqual(0, controller.PacketsSent);
            Assert.IsFalse(controller.TryGetLastPacket(new byte[DeviceProtocol.PacketSize]));
        }

        [Test]
        public void Enabled_ControllerAndLightAreOn()
        {
            _root = new GameObject("DeviceTest");
            var controller = _root.AddComponent<DeviceController>();
            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(_root.transform);
            var light = lightObject.AddComponent<VirtualMovingLight>();
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(_root.transform);
            var camera = cameraObject.AddComponent<Camera>();

            controller.Initialize(new AppConfig { deviceEnabled = true }, null, new LogPacketSink(false));
            light.Initialize(controller, camera);

            Assert.IsTrue(controller.IsActive);
            Assert.IsTrue(lightObject.activeSelf);
            Assert.AreEqual(1, lightObject.layer);
        }
    }
}
