using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Cantio.Models;

namespace Cantio.Services.Devices;

/// <summary>
/// Sterownik projektorów PJLink klasa 1 (TCP, domyślnie port 4352).
/// Jedno połączenie per operacja: connect → auth → komenda → odpowiedź → close.
/// </summary>
public sealed class PjLinkDriver : IDisplayDeviceDriver
{
    private const int ConnectTimeoutMs = 3000;
    private const int IoTimeoutMs = 3000;

    public async Task<bool> PowerOnAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        var (ok, resp) = await SendCommandAsync(device, "%1POWR 1", ct);
        return ok && resp.Contains("=OK", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> PowerOffAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        var (ok, resp) = await SendCommandAsync(device, "%1POWR 0", ct);
        return ok && resp.Contains("=OK", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DevicePowerState> GetStateAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        var (ok, resp) = await SendCommandAsync(device, "%1POWR ?", ct);
        if (!ok) return DevicePowerState.Unknown;
        return ParsePowerQuery(resp);
    }

    /// <summary>Test połączenia do przycisku „Testuj" w UI — zwraca czytelny komunikat.</summary>
    public async Task<(bool ok, string message)> TestAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        try
        {
            var (ok, resp) = await SendCommandAsync(device, "%1POWR ?", ct);
            if (!ok)
            {
                if (resp.Contains("ERRA", StringComparison.OrdinalIgnoreCase))
                    return (false, "Błędne hasło (autoryzacja odrzucona)");
                if (resp.StartsWith("TIMEOUT", StringComparison.OrdinalIgnoreCase))
                    return (false, "Przekroczono czas oczekiwania — sprawdź IP/port");
                return (false, string.IsNullOrEmpty(resp) ? "Brak odpowiedzi" : resp);
            }
            var state = ParsePowerQuery(resp);
            var human = state switch
            {
                DevicePowerState.On => "włączony",
                DevicePowerState.Off => "wyłączony / standby",
                _ => "nieznany"
            };
            return (true, $"OK — projektor {human}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static DevicePowerState ParsePowerQuery(string resp)
    {
        // %1POWR=0 standby, =1 on, =2 cooling→Off, =3 warmup→On
        int eq = resp.IndexOf('=');
        if (eq < 0 || eq + 1 >= resp.Length) return DevicePowerState.Unknown;
        return resp[eq + 1] switch
        {
            '1' or '3' => DevicePowerState.On,
            '0' or '2' => DevicePowerState.Off,
            _ => DevicePowerState.Unknown
        };
    }

    /// <summary>
    /// Otwiera połączenie, obsługuje handshake/auth i wysyła jedną komendę.
    /// Zwraca (ok=czy odpowiedź to poprawna odpowiedź komendy, raw=surowa treść).
    /// Przy błędach zwraca ok=false i komunikat ("TIMEOUT...", "ERRA...", itd.).
    /// </summary>
    private static async Task<(bool ok, string raw)> SendCommandAsync(
        ProjectionDevice device, string command, CancellationToken ct)
    {
        int port = device.Port > 0 ? device.Port : 4352;
        try
        {
            using var client = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeoutMs);
            try { await client.ConnectAsync(device.Ip, port, connectCts.Token); }
            catch (OperationCanceledException) { return (false, "TIMEOUT connect"); }

            using var stream = client.GetStream();
            using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ioCts.CancelAfter(IoTimeoutMs);

            // Serwer wysyła linię powitalną: "PJLINK 0" lub "PJLINK 1 <random>".
            var greeting = await ReadLineAsync(stream, ioCts.Token);
            string prefix = "";
            var parts = greeting.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && parts[0].Equals("PJLINK", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length >= 2 && parts[1] == "1")
                {
                    if (parts.Length < 3) return (false, "PJLINK auth bez losowej wartości");
                    var random = parts[2];
                    prefix = Md5Hex(random + (device.Password ?? ""));
                }
            }

            var payload = Encoding.ASCII.GetBytes(prefix + command + "\r");
            await stream.WriteAsync(payload, ioCts.Token);

            var response = await ReadLineAsync(stream, ioCts.Token);
            if (response.Contains("ERRA", StringComparison.OrdinalIgnoreCase))
                return (false, "ERRA (złe hasło)");
            // ERR3/ERR4 to błędy komendy (projektor zajęty / nieobsługiwana)
            if (response.Contains("=ERR", StringComparison.OrdinalIgnoreCase))
                return (false, response);
            return (true, response);
        }
        catch (OperationCanceledException)
        {
            return (false, "TIMEOUT");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PjLinkDriver] {command} failed: {ex.Message}");
            return (false, ex.Message);
        }
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (sb.Length < 256)
        {
            int n = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
            if (n == 0) break;
            char c = (char)buf[0];
            if (c == '\r' || c == '\n') { if (sb.Length > 0) break; else continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Md5Hex(string input)
    {
        var hash = MD5.HashData(Encoding.ASCII.GetBytes(input));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
