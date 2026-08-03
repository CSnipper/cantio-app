using Cantio.Models;

namespace Cantio.Services.Devices;

public enum DevicePowerState { Unknown, On, Off }

/// <summary>
/// Sterownik jednego typu urządzenia projekcyjnego. Implementacje muszą być
/// odporne na błędy sieci — nie rzucać wyjątków przy braku łączności.
/// </summary>
public interface IDisplayDeviceDriver
{
    Task<bool> PowerOnAsync(ProjectionDevice device, CancellationToken ct = default);
    Task<bool> PowerOffAsync(ProjectionDevice device, CancellationToken ct = default);
    Task<DevicePowerState> GetStateAsync(ProjectionDevice device, CancellationToken ct = default);
}
