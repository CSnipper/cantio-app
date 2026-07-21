using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services.Devices;

/// <summary>
/// Sterownik telewizorów Sony Bravia sterowanych lokalnie przez REST API
/// (POST http://IP/sony/system, nagłówek X-Auth-PSK). Włączanie z głębokiego
/// standby wspierane fallbackiem Wake-on-LAN (jeśli podano MAC).
/// </summary>
public sealed class SonyBraviaDriver : IDisplayDeviceDriver
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private const string GetPower =
        "{\"method\":\"getPowerStatus\",\"id\":50,\"params\":[],\"version\":\"1.0\"}";
    private const string SetPowerOn =
        "{\"method\":\"setPowerStatus\",\"id\":55,\"params\":[{\"status\":true}],\"version\":\"1.0\"}";
    private const string SetPowerOff =
        "{\"method\":\"setPowerStatus\",\"id\":55,\"params\":[{\"status\":false}],\"version\":\"1.0\"}";

    public async Task<DevicePowerState> GetStateAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(device.Ip)) return DevicePowerState.Unknown;
        var (ok, body, timedOut) = await PostAsync(device, GetPower, ct);
        if (!ok)
            return timedOut ? DevicePowerState.Off : DevicePowerState.Unknown;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out _))
                return DevicePowerState.Unknown; // np. zły PSK (403)
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0 &&
                result[0].TryGetProperty("status", out var status))
            {
                var val = status.GetString();
                if (string.Equals(val, "active", StringComparison.OrdinalIgnoreCase))
                    return DevicePowerState.On;
                if (string.Equals(val, "standby", StringComparison.OrdinalIgnoreCase))
                    return DevicePowerState.Off;
            }
        }
        catch { }
        return DevicePowerState.Unknown;
    }

    public async Task<bool> PowerOnAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        var (ok, body, _) = await PostAsync(device, SetPowerOn, ct);
        if (ok && !HasError(body))
            return true;

        // Głęboki standby (Remote Start off / brak odpowiedzi) — fallback WoL.
        if (string.IsNullOrWhiteSpace(device.Mac))
            return false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(16));

            var wol = Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try { await WolDriver.SendMagicPacketAsync(device.Mac, cts.Token); } catch { }
                    if (i < 4) await Task.Delay(2000, cts.Token);
                }
            }, cts.Token);

            for (int i = 0; i < 8; i++)
            {
                if (await GetStateAsync(device, cts.Token) == DevicePowerState.On)
                    return true;
                try { await Task.Delay(2000, cts.Token); } catch { break; }
            }
            return await GetStateAsync(device, ct) == DevicePowerState.On;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SonyBraviaDriver] PowerOn WoL fallback failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> PowerOffAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        var (ok, body, _) = await PostAsync(device, SetPowerOff, ct);
        return ok && !HasError(body);
    }

    /// <summary>Test połączenia do przycisku „Testuj" w UI — zwraca czytelny komunikat.</summary>
    public async Task<(bool ok, string message)> TestAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        try
        {
            var (ok, body, timedOut) = await PostAsync(device, GetPower, ct);
            if (!ok)
            {
                if (timedOut)
                    return (false, "Przekroczono czas oczekiwania — sprawdź IP/połączenie");
                return (false, string.IsNullOrEmpty(body) ? "Brak odpowiedzi" : body);
            }
            if (HasError(body) || body.Contains("403") ||
                body.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
                return (false, "Błędny klucz PSK (autoryzacja odrzucona)");

            var state = ParseState(body);
            var human = state switch
            {
                DevicePowerState.On => "włączony",
                DevicePowerState.Off => "wyłączony / standby",
                _ => "nieznany"
            };
            return (true, $"OK — telewizor {human}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static DevicePowerState ParseState(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0 &&
                result[0].TryGetProperty("status", out var status))
            {
                var val = status.GetString();
                if (string.Equals(val, "active", StringComparison.OrdinalIgnoreCase))
                    return DevicePowerState.On;
                if (string.Equals(val, "standby", StringComparison.OrdinalIgnoreCase))
                    return DevicePowerState.Off;
            }
        }
        catch { }
        return DevicePowerState.Unknown;
    }

    private static bool HasError(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var err) &&
                   err.ValueKind != JsonValueKind.Null &&
                   !(err.ValueKind == JsonValueKind.Array && err.GetArrayLength() == 0);
        }
        catch { return false; }
    }

    /// <summary>
    /// Wysyła jedno żądanie POST do /sony/system. Zwraca
    /// (ok=czy dostaliśmy odpowiedź HTTP, body=treść, timedOut=czy przekroczono czas/brak łączności).
    /// </summary>
    private static async Task<(bool ok, string body, bool timedOut)> PostAsync(
        ProjectionDevice device, string json, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{device.Ip}/sony/system")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("X-Auth-PSK", device.Password ?? "");

            using var resp = await Http.SendAsync(req, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            return (true, body, false);
        }
        catch (OperationCanceledException)
        {
            return (false, "", true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SonyBraviaDriver] POST failed: {ex.Message}");
            return (false, ex.Message, false);
        }
    }
}
