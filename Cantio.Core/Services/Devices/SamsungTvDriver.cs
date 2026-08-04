using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services.Devices;

/// <summary>
/// Sterownik telewizorów Samsung Tizen sterowanych lokalnie:
/// - stan/parowanie/wyłączanie przez WebSocket remote (port 8001/8002),
/// - włączanie przez Wake-on-LAN (TV w standby nie odpowiada na WS).
/// </summary>
public sealed class SamsungTvDriver : IDisplayDeviceDriver
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly string NameB64 =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("Cantio"));

    public async Task<DevicePowerState> GetStateAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(device.Ip)) return DevicePowerState.Unknown;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var json = await Http.GetStringAsync($"http://{device.Ip}:8001/api/v2/", cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("device", out var dev) &&
                dev.TryGetProperty("PowerState", out var ps))
            {
                var val = ps.GetString();
                if (string.Equals(val, "on", StringComparison.OrdinalIgnoreCase))
                    return DevicePowerState.On;
                if (string.Equals(val, "standby", StringComparison.OrdinalIgnoreCase))
                    return DevicePowerState.Off;
            }
            // Starsze modele bez PowerState — skoro odpowiada, jest włączony.
            return DevicePowerState.On;
        }
        catch
        {
            // Brak odpowiedzi to "nie wiem", a nie "wyłączony". TV z włączonym
            // zdalnym włączaniem trzyma moduł sieciowy żywy nawet w standby i
            // ODPOWIADA wtedy "PowerState: standby" — realny standby wraca
            // normalną ścieżką wyżej jako Off, nie przez ten catch. Tu wpada
            // przede wszystkim zerwana sieć albo blokada na routerze.
            return DevicePowerState.Unknown;
        }
    }

    public async Task<bool> PowerOnAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(device.Mac)) return false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(16));

            // WoL w tle: 5 pakietów co 2 s.
            var wol = Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try { await WolDriver.SendMagicPacketAsync(device.Mac, cts.Token); } catch { }
                    if (i < 4) await Task.Delay(2000, cts.Token);
                }
            }, cts.Token);

            // Polling stanu co 2 s, max 15 s.
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
            AppLog.Write("SamsungTvDriver", $"{device.Ip} PowerOn failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> PowerOffAsync(ProjectionDevice device, CancellationToken ct = default)
    {
        try
        {
            var (ok, _, _) = await RemoteSessionAsync(device, "KEY_POWER", ct);
            return ok;
        }
        catch (Exception ex)
        {
            AppLog.Write("SamsungTvDriver", $"{device.Ip} PowerOff failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Parowanie: łączy się jak przy sterowaniu, ale bez wysyłania klawisza.
    /// Zwraca token po akceptacji na TV (użytkownik musi kliknąć „Zezwól").
    /// </summary>
    public async Task<(bool ok, string? token, string? error)> PairAsync(
        ProjectionDevice device, CancellationToken ct = default)
        => await RemoteSessionAsync(device, sendKey: null, ct);

    /// <summary>
    /// Otwiera sesję WebSocket remote control. Czeka na event ms.channel.connect
    /// (może zawierać nowy token). Opcjonalnie wysyła jeden klawisz.
    /// </summary>
    private static async Task<(bool ok, string? token, string? error)> RemoteSessionAsync(
        ProjectionDevice device, string? sendKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(device.Ip))
            return (false, null, "Brak adresu IP");

        var url = $"wss://{device.Ip}:8002/api/v2/channels/samsung.remote.control?name={NameB64}";
        if (!string.IsNullOrEmpty(device.Token))
            url += $"&token={device.Token}";

        using var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // czas na kliknięcie „Zezwól" na TV

        try
        {
            await ws.ConnectAsync(new Uri(url), cts.Token);

            string? newToken = null;
            bool connected = false;
            var buf = new byte[8192];

            // Czekaj na ms.channel.connect.
            while (!connected && !cts.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buf, cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return (false, null, "TV zamknął połączenie (odmowa?)");
                    ms.Write(buf, 0, result.Count);
                } while (!result.EndOfMessage);

                try
                {
                    using var doc = JsonDocument.Parse(ms.ToArray());
                    var ev = doc.RootElement.TryGetProperty("event", out var e) ? e.GetString() : null;
                    if (ev == "ms.channel.connect")
                    {
                        connected = true;
                        if (doc.RootElement.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("token", out var tok) &&
                            tok.ValueKind == JsonValueKind.String)
                            newToken = tok.GetString();
                    }
                    else if (ev == "ms.channel.unauthorized")
                    {
                        return (false, null, "TV odrzucił połączenie (brak autoryzacji)");
                    }
                }
                catch { /* nie-JSON lub inny event — pomiń */ }
            }

            if (!connected)
                return (false, null, "Przekroczono czas — czy zaakceptowano na TV?");

            if (sendKey is not null)
            {
                var cmd = JsonSerializer.Serialize(new
                {
                    method = "ms.remote.control",
                    @params = new
                    {
                        Cmd = "Click",
                        DataOfCmd = sendKey,
                        Option = "false",
                        TypeOfRemote = "SendRemoteKey"
                    }
                });
                await ws.SendAsync(Encoding.UTF8.GetBytes(cmd),
                    WebSocketMessageType.Text, true, cts.Token);
                await Task.Delay(300, cts.Token);
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
            catch { }

            return (true, string.IsNullOrEmpty(newToken) ? device.Token : newToken, null);
        }
        catch (OperationCanceledException)
        {
            return (false, null, "Przekroczono czas — czy zaakceptowano na TV?");
        }
        catch (Exception ex)
        {
            // Świadomie bez URL-a — zawiera token parowania, który jest sekretem.
            AppLog.Write("SamsungTvDriver", $"{device.Ip} RemoteSession failed: {ex.Message}");
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Odpytuje TV pod danym IP o nazwę i adres MAC Wi-Fi (HTTP /api/v2/).
    /// Zwraca (null, null) jeśli TV nie odpowiada lub nie jest Samsungiem Tizen.
    /// Używane zarówno przez SSDP-discovery jak i ręczne dodanie po IP.
    /// </summary>
    public static async Task<(string? name, string? mac)> GetInfoAsync(string ip, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var json = await Http.GetStringAsync($"http://{ip}:8001/api/v2/", cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("device", out var dev))
            {
                var name = dev.TryGetProperty("name", out var n) ? n.GetString() : null;
                var mac = dev.TryGetProperty("wifiMac", out var m) ? m.GetString() : null;
                return (name, mac);
            }
        }
        catch { }
        return (null, null);
    }

    /// <summary>
    /// Wykrywanie TV Samsung przez SSDP M-SEARCH. Wysyła zapytania ze WSZYSTKICH aktywnych
    /// lokalnych interfejsów IPv4 (router/AP potrafi blokować multicast na wybranym interfejsie),
    /// zarówno z dedykowanym ST Samsunga jak i ssdp:all. Dla każdego respondera weryfikuje
    /// przez HTTP /api/v2/ (dedup po IP) — to jednocześnie filtruje odpowiedzi ssdp:all do
    /// realnych TV Samsung.
    /// </summary>
    public static async Task<List<(string name, string ip, string mac)>> DiscoverAsync(
        TimeSpan timeout, CancellationToken ct = default)
    {
        var results = new List<(string name, string ip, string mac)>();
        var seenIps = new HashSet<string>();
        var candidateIps = new HashSet<string>();
        var clients = new List<UdpClient>();

        try
        {
            // Wykrywanie zawodzi najczęściej na poziomie interfejsów (izolacja klientów, VPN,
            // wirtualne karty) — bez tych trzech linii w logu nie da się tego zdiagnozować zdalnie.
            var boundIps = new List<string>();
            foreach (var localIp in GetLocalIPv4Addresses())
            {
                try
                {
                    var udp = new UdpClient();
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(localIp, 0));
                    clients.Add(udp);
                    boundIps.Add(localIp.ToString());
                }
                catch (Exception ex)
                {
                    AppLog.Write("SamsungTvDriver", $"Bind failed on {localIp}: {ex.Message}");
                }
            }
            AppLog.Write("SamsungTvDriver",
                $"Discover: M-SEARCH z {boundIps.Count} interfejsów IPv4 [{string.Join(", ", boundIps)}]");

            // Brak wykrytych interfejsów z bramą (rzadkie) — spróbuj domyślnego socketu.
            if (clients.Count == 0)
            {
                try
                {
                    var udp = new UdpClient();
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                    clients.Add(udp);
                }
                catch (Exception ex)
                {
                    AppLog.Write("SamsungTvDriver", $"Fallback bind failed: {ex.Message}");
                }
            }

            var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            const string msearchTemplate =
                "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: 239.255.255.250:1900\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 3\r\n" +
                "ST: {0}\r\n\r\n";

            foreach (var st in new[] { "urn:samsung.com:device:RemoteControlReceiver:1", "ssdp:all" })
            {
                var msearchBytes = Encoding.ASCII.GetBytes(string.Format(msearchTemplate, st));
                foreach (var udp in clients)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try { await udp.SendAsync(msearchBytes, msearchBytes.Length, target); } catch { }
                    }
                }
                await Task.Delay(200, ct);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var receiveTasks = clients.Select(udp => Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        var recv = await udp.ReceiveAsync(cts.Token);
                        lock (candidateIps)
                            candidateIps.Add(recv.RemoteEndPoint.Address.ToString());
                    }
                }
                catch (OperationCanceledException) { /* koniec okna zbierania */ }
                catch (Exception ex)
                {
                    AppLog.Write("SamsungTvDriver", $"Receive failed: {ex.Message}");
                }
            }, ct)).ToArray();

            await Task.WhenAll(receiveTasks);

            AppLog.Write("SamsungTvDriver",
                $"Discover: {candidateIps.Count} odpowiedzi SSDP [{string.Join(", ", candidateIps)}]");

            // Pobierz szczegóły per IP (równolegle) — weryfikuje, że to faktycznie Samsung TV.
            var tasks = candidateIps.Select(async ip =>
            {
                var (name, mac) = await GetInfoAsync(ip, ct);
                if (name is null) return null;
                return ((string name, string ip, string mac)?)(name, ip, mac ?? "");
            });

            foreach (var r in await Task.WhenAll(tasks))
            {
                if (r is { } v && seenIps.Add(v.ip))
                    results.Add(v);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("SamsungTvDriver", $"Discover failed: {ex.Message}");
        }
        finally
        {
            foreach (var udp in clients)
                try { udp.Dispose(); } catch { }
        }

        AppLog.Write("SamsungTvDriver",
            $"Discover: wykryto {results.Count} TV [{string.Join(", ", results.Select(r => r.ip))}]");
        return results;
    }

    /// <summary>
    /// Adresy IPv4 aktywnych interfejsów z bramą domyślną (pomija loopback/wyłączone).
    /// Jeden zepsuty interfejs nie ubija wykrywania na pozostałych.
    /// </summary>
    private static List<IPAddress> GetLocalIPv4Addresses()
    {
        var addrs = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;

                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            addrs.Add(ua.Address);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write("SamsungTvDriver", $"Interface enum failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("SamsungTvDriver", $"GetLocalIPv4Addresses failed: {ex.Message}");
        }
        return addrs;
    }
}
