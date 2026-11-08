using System;
using VisionToWall.Core;
using VisionToWall.Device;
using VisionToWall.UI;
using VisionToWall.Weather;

namespace VisionToWall.App
{
    /// <summary>UI references Core only; these bridge the F1 panel's roadmap rows to Device and Weather.</summary>
    internal sealed class DevicePanelAdapter : IDevicePanelSource
    {
        private readonly DeviceController _device;
        public DevicePanelAdapter(DeviceController device) => _device = device;
        public bool IsActive => _device != null && _device.IsActive;
        public int PacketsSent => _device != null ? _device.PacketsSent : 0;
        public bool TryGetLastPacket(Span<byte> destination) => _device != null && _device.TryGetLastPacket(destination);
    }

    internal sealed class WeatherPanelAdapter : IWeatherPanelSource
    {
        private readonly WeatherService _service;
        public WeatherPanelAdapter(WeatherService service) => _service = service;
        public WeatherKind Current => _service != null ? _service.Current : WeatherKind.Unknown;
        public bool IsOverride => _service != null && _service.IsOverride;
        public DateTime LastSuccessUtc =>
            _service != null && _service.Poller != null ? _service.Poller.LastSuccessUtc : DateTime.MinValue;

        public WeatherPanelState State
        {
            get
            {
                WeatherPoller p = _service != null ? _service.Poller : null;
                if (p == null) return WeatherPanelState.Off;
                switch (p.LiveStatus)
                {
                    case WeatherFetchStatus.Ok: return WeatherPanelState.Ok;
                    case WeatherFetchStatus.NoKey: return WeatherPanelState.NoKey;
                    case WeatherFetchStatus.Network:
                    case WeatherFetchStatus.Timeout: return WeatherPanelState.Offline;
                    case WeatherFetchStatus.Auth: return WeatherPanelState.AuthError;
                    case WeatherFetchStatus.ServiceError:
                    case WeatherFetchStatus.NoData:
                    case WeatherFetchStatus.Malformed: return WeatherPanelState.ServiceError;
                    default: return p.LiveAvailable ? WeatherPanelState.Waiting : WeatherPanelState.Off;
                }
            }
        }
    }
}
