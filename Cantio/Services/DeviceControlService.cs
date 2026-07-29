using System.Text.Json;
using Cantio.Models;
using Cantio.Services.Devices;

namespace Cantio.Services;

/// <summary>
/// Fasada dla ViewModeli: przechowuje listę urządzeń projekcyjnych (JSON w ustawieniu
/// <c>projection_devices</c>) i deleguje sterowanie do odpowiedniego sterownika.
/// Wszystkie operacje sieciowe są odporne na błędy — zwracają false/Unknown zamiast rzucać.
/// </summary>
public sealed class DeviceControlService
{
    private const string SettingKey = "projection_devices";

    private readonly DatabaseService _db;
    private readonly WolDriver _wol = new();
    private readonly PjLinkDriver _pjlink = new();
    private readonly SamsungTvDriver _samsung = new();
    private readonly SonyBraviaDriver _sony = new();

    public DeviceControlService(DatabaseService db) => _db = db;

    public async Task<List<ProjectionDevice>> GetDevicesAsync()
    {
        var json = await _db.GetSettingAsync(SettingKey);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<ProjectionDevice>>(json) ?? []; }
        catch { return []; }
    }

    public async Task SaveDevicesAsync(List<ProjectionDevice> devices)
    {
        var json = JsonSerializer.Serialize(devices);
        await _db.SaveSettingAsync(SettingKey, json);
    }

    public IDisplayDeviceDriver GetDriver(ProjectionDevice device) => device.Type switch
    {
        "samsung" => _samsung,
        "pjlink" => _pjlink,
        "sony" => _sony,
        _ => _wol,
    };

    /// <summary>
    /// Opis urządzenia do logu. Log ma wskazywać KTÓRE urządzenie zawiodło —
    /// samo „coś padło" nic nie dawało przy diagnozie u użytkownika.
    /// </summary>
    private static string Describe(ProjectionDevice device)
    {
        var name = string.IsNullOrWhiteSpace(device.Name) ? "(bez nazwy)" : device.Name;
        var ip = string.IsNullOrWhiteSpace(device.Ip) ? device.Mac : device.Ip;
        return string.IsNullOrWhiteSpace(ip) ? $"{name} [{device.Type}]" : $"{name} [{device.Type} {ip}]";
    }

    public async Task<bool> PowerOnAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        try
        {
            var ok = await GetDriver(device).PowerOnAsync(device, ct);
            AppLog.Write("DeviceControl", $"PowerOn {Describe(device)} → {(ok ? "OK" : "nieudane")}");
            return ok;
        }
        catch (Exception ex)
        {
            AppLog.Write("DeviceControl", $"PowerOn {Describe(device)} → wyjątek: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> PowerOffAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        try
        {
            var ok = await GetDriver(device).PowerOffAsync(device, ct);
            AppLog.Write("DeviceControl", $"PowerOff {Describe(device)} → {(ok ? "OK" : "nieudane")}");
            return ok;
        }
        catch (Exception ex)
        {
            AppLog.Write("DeviceControl", $"PowerOff {Describe(device)} → wyjątek: {ex.Message}");
            return false;
        }
    }

    public async Task<DevicePowerState> GetStateAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        try { return await GetDriver(device).GetStateAsync(device, ct); }
        catch (Exception ex)
        {
            AppLog.Write("DeviceControl", $"GetState {Describe(device)} → wyjątek: {ex.Message}");
            return DevicePowerState.Unknown;
        }
    }

    /// <summary>Włącza/wyłącza wszystkie zapisane urządzenia równolegle.</summary>
    public async Task PowerAllAsync(bool on, CancellationToken ct = default)
    {
        var devices = await GetDevicesAsync();
        var tasks = devices.Select(d => on ? PowerOnAsync(d, ct) : PowerOffAsync(d, ct));
        await Task.WhenAll(tasks);
    }
}
