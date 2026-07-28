using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cantio.Services;

public sealed class RemoteControlServer : IDisposable
{
    private TcpListener? _listener;
    private UdpClient? _udpDiscovery;
    private CancellationTokenSource _cts = new();
    private readonly List<WebSocket> _clients = [];
    private readonly Lock _lock = new();

    // ─── Parowanie PIN-em ───────────────────────────────────────────────
    /// <summary>Maksymalna liczba nieudanych prób w ramach jednego połączenia.</summary>
    public const int MaxAttemptsPerConnection = 5;
    /// <summary>Maksymalna liczba przechowywanych tokenów sparowanych urządzeń.</summary>
    public const int MaxTokens = 20;

    private readonly HashSet<string> _tokens = [];
    private readonly Dictionary<string, IpFailure> _ipFailures = [];

    private sealed class IpFailure { public int Count; public DateTime LockedUntil; public DateTime LastFail; }

    /// <summary>Gdy false — serwer działa bez uwierzytelniania (jak przed v1.56).</summary>
    public bool RequirePin { get; set; } = true;
    /// <summary>4-cyfrowy PIN parowania.</summary>
    public string Pin { get; set; } = "";
    /// <summary>Po ilu nieudanych próbach z jednego IP włącza się blokada.</summary>
    public int MaxIpFailures { get; set; } = 10;
    /// <summary>Czas blokady adresu IP po przekroczeniu limitu prób.</summary>
    public TimeSpan IpLockout { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Czas na uwierzytelnienie od nawiązania połączenia.</summary>
    public TimeSpan AuthTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Nowy token wydany sparowanemu urządzeniu — do zapisania w ustawieniach.</summary>
    public event Action<string>? TokenIssued;
    /// <summary>Odrzucono niesparowane urządzenie (IP + powód) — do logu/UI.</summary>
    public event Action<string>? ClientRejected;

    public event EventHandler? NextRequested;
    public event EventHandler? PrevRequested;
    public event EventHandler? BlankRequested;
    public event Action<int>? GotoRequested;
    public event Action<int>? GotoSongRequested;
    public event Action<int>? SetlistAddRequested;      // songId
    public event Action<int>? SetlistRemoveRequested;   // index
    public event Action<int, int>? SetlistMoveRequested; // from, to
    public event Action<WebSocket, int, int>? GetSongsRequested; // ws, offset, limit
    public event Action<WebSocket, string>? SyncPushRequested;  // ws, raw json
    public event Action? SetlistClearRequested;
    public event Action<int[]>? SetlistRestoreRequested;        // songIds
    public event Action<WebSocket>? GetSetlistsRequested;       // ws
    public event Action<WebSocket, int>? OpenSetlistRequested;  // ws, setlistId
    public event Action<WebSocket, int>? GetSetlistDetailRequested; // ws, setlistId
    public event Action<WebSocket, string>? SetlistSyncPushRequested; // ws, raw json
    public event Action<WebSocket, int>? SetlistDeleteRequested;      // ws, desktopId
    public event Action<bool>? DevicesPowerAllRequested;            // on/off wszystkie urządzenia
    public event Action<WebSocket>? ClientConnected;
    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    public void Start(int port = 8765)
    {
        if (IsRunning) return;
        Port = port;
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        IsRunning = true;
        _ = AcceptLoopAsync(_cts.Token);
        _ = UdpDiscoveryLoopAsync(_cts.Token);
    }

    /// <summary>Wczytuje tokeny sparowanych urządzeń (z ustawień).</summary>
    public void SetTokens(IEnumerable<string> tokens)
    {
        lock (_lock)
        {
            _tokens.Clear();
            foreach (var t in tokens)
                if (!string.IsNullOrWhiteSpace(t)) _tokens.Add(t);
        }
    }

    /// <summary>Unieważnia wszystkie sparowane urządzenia (wywoływane przy zmianie PIN-u).</summary>
    public void ClearTokens()
    {
        lock (_lock) _tokens.Clear();
    }

    /// <summary>Rozłącza wszystkich klientów — muszą się uwierzytelnić od nowa.</summary>
    public void DisconnectAllClients()
    {
        List<WebSocket> toClose;
        lock (_lock) { toClose = [.. _clients]; _clients.Clear(); }
        foreach (var ws in toClose)
            try { ws.Abort(); } catch { }
    }

    /// <summary>Losowy 4-cyfrowy PIN (kryptograficznie).</summary>
    public static string GeneratePin() => RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");

    /// <summary>Losowy token urządzenia (256 bitów, base64url).</summary>
    public static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public void Stop()
    {
        _cts.Cancel();
        _listener?.Stop();
        _listener = null;
        _udpDiscovery?.Dispose();
        _udpDiscovery = null;
        IsRunning = false;
        List<WebSocket> toClose;
        lock (_lock) { toClose = [.. _clients]; _clients.Clear(); _ipFailures.Clear(); }
        foreach (var ws in toClose)
            try { ws.Dispose(); } catch { }
    }

    public async Task BroadcastAsync(
        string text, string songTitle, int index, int total,
        bool isBlank = false, IList<string>? slides = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "slide", text, songTitle, index, total,
            isBlank,
            slides = slides ?? []
        });
        await BroadcastRawAsync(json);
    }

    public async Task BroadcastSetlistAsync(
        IList<(int id, string title)> songs, int activeIndex)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "setlist",
            activeIndex,
            songs = songs.Select(s => new { id = s.id, title = s.title }).ToList()
        });
        await BroadcastRawAsync(json);
    }

    /// <summary>Rozgłasza zbiorczy stan urządzeń projekcyjnych do pilotów.</summary>
    public async Task BroadcastDevicesAsync(string state, int count)
    {
        var json = JsonSerializer.Serialize(new { type = "devices", state, count });
        await BroadcastRawAsync(json);
    }

    public Task SendToClientAsync(WebSocket ws, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return ws.State == WebSocketState.Open
            ? ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None)
            : Task.CompletedTask;
    }

    private async Task BroadcastRawAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        List<WebSocket> snapshot;
        lock (_lock) snapshot = [.. _clients];
        foreach (var ws in snapshot)
            if (ws.State == WebSocketState.Open)
                try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { /* client disconnected */ }
    }

    // Nasłuchuje UDP broadcast na porcie Port+1, odpowiada na {"type":"discover"}
    private async Task UdpDiscoveryLoopAsync(CancellationToken ct)
    {
        int discoveryPort = Port + 1;
        try
        {
            _udpDiscovery = new UdpClient(discoveryPort);
            var response = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { type = "cantio", port = Port }));
            while (!ct.IsCancellationRequested)
            {
                var result = await _udpDiscovery.ReceiveAsync(ct);
                try
                {
                    var doc = JsonDocument.Parse(result.Buffer);
                    if (doc.RootElement.GetProperty("type").GetString() == "discover")
                        await _udpDiscovery.SendAsync(response, result.RemoteEndPoint, ct);
                }
                catch { }
            }
        }
        catch { }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var (headers, _) = await ReadHttpHeadersAsync(stream, ct);

            if (headers.TryGetValue("Upgrade", out var upgrade) &&
                upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
            {
                await DoWebSocketHandshakeAsync(stream, headers, ct);
                var ws = WebSocket.CreateFromStream(stream,
                    isServer: true, subProtocol: null,
                    keepAliveInterval: TimeSpan.FromSeconds(30));
                var ip = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
                try { await ReceiveLoopAsync(ws, ip, ct); }
                finally
                {
                    lock (_lock) _clients.Remove(ws);
                    ws.Dispose();
                }
            }
            else
            {
                await ServeHtmlAsync(stream, ct);
            }
        }
        catch { /* client disconnected mid-handshake */ }
    }

    private static async Task<(Dictionary<string, string> Headers, string RequestLine)>
        ReadHttpHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[4096];
        int total = 0;
        while (total < buf.Length)
        {
            int n = await stream.ReadAsync(buf.AsMemory(total, 1), ct);
            if (n == 0) break;
            total++;
            if (total >= 4 &&
                buf[total - 4] == '\r' && buf[total - 3] == '\n' &&
                buf[total - 2] == '\r' && buf[total - 1] == '\n')
                break;
        }

        var lines = Encoding.UTF8.GetString(buf, 0, total)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var requestLine = lines.Length > 0 ? lines[0] : "";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0)
                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }
        return (headers, requestLine);
    }

    private static async Task DoWebSocketHandshakeAsync(
        NetworkStream stream, Dictionary<string, string> headers, CancellationToken ct)
    {
        if (!headers.TryGetValue("Sec-WebSocket-Key", out var key) || string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Missing Sec-WebSocket-Key");
        var accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var response =
            $"HTTP/1.1 101 Switching Protocols\r\n" +
            $"Upgrade: websocket\r\n" +
            $"Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
    }

    private static async Task ServeHtmlAsync(NetworkStream stream, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(GetHtml());
        var header =
            $"HTTP/1.1 200 OK\r\n" +
            $"Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            $"Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
        await stream.WriteAsync(body, ct);
    }

    private async Task ReceiveLoopAsync(WebSocket ws, string ip, CancellationToken ct)
    {
        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bool authed = !RequirePin;
        int attempts = 0;

        if (authed)
        {
            lock (_lock) _clients.Add(ws);
            ClientConnected?.Invoke(ws);
        }
        else
        {
            authCts.CancelAfter(AuthTimeout);
            await SafeSendAsync(ws, """{"type":"auth_required"}""");
        }

        var loopCt = authCts.Token;
        var buf = new byte[1024];
        while (ws.State == WebSocketState.Open && !loopCt.IsCancellationRequested)
        {
            using var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await ws.ReceiveAsync(buf, loopCt);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buf, 0, result.Count);
                } while (!result.EndOfMessage);
            }
            catch
            {
                // Klient (np. stara wersja Pilota) nie odpowiedział na auth_required
                if (!authed && authCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    Reject(ip, $"brak uwierzytelnienia w {AuthTimeout.TotalSeconds:0} s");
                    await CloseRejectedAsync(ws);
                }
                break;
            }

            try
            {
                var doc = JsonDocument.Parse(ms.ToArray());
                var type = doc.RootElement.GetProperty("type").GetString();

                if (type == "auth")
                {
                    if (authed)
                    {
                        // PIN wyłączony albo już uwierzytelniony — wydaj token na przyszłość
                        await SendAuthOkAsync(ws, IssueToken());
                        continue;
                    }
                    int retryAfter = LockRemainingSeconds(ip);
                    if (retryAfter > 0)
                    {
                        await SafeSendAsync(ws, AuthFailedJson(retryAfter));
                        Reject(ip, $"adres zablokowany na {retryAfter} s");
                        await CloseRejectedAsync(ws);
                        return;
                    }

                    string? token = doc.RootElement.TryGetProperty("token", out var tEl)
                        ? tEl.GetString() : null;
                    string? pin = doc.RootElement.TryGetProperty("pin", out var pEl)
                        ? pEl.GetString() : null;

                    string? accepted = null;
                    if (!string.IsNullOrEmpty(token) && TokenKnown(token)) accepted = token;
                    else if (!string.IsNullOrEmpty(pin) && PinMatches(pin)) accepted = IssueToken();

                    if (accepted != null)
                    {
                        authed = true;
                        authCts.CancelAfter(Timeout.InfiniteTimeSpan);
                        ClearIpFailures(ip);
                        await SendAuthOkAsync(ws, accepted);
                        lock (_lock) _clients.Add(ws);
                        ClientConnected?.Invoke(ws);
                    }
                    else
                    {
                        attempts++;
                        RegisterIpFailure(ip);
                        await SafeSendAsync(ws, AuthFailedJson(LockRemainingSeconds(ip)));
                        if (attempts >= MaxAttemptsPerConnection)
                        {
                            Reject(ip, $"{attempts} nieudanych prób PIN");
                            await CloseRejectedAsync(ws);
                            return;
                        }
                    }
                    continue;
                }

                if (!authed) continue;   // komendy przed uwierzytelnieniem — ignorowane

                if (type == "next") NextRequested?.Invoke(this, EventArgs.Empty);
                else if (type == "prev") PrevRequested?.Invoke(this, EventArgs.Empty);
                else if (type == "blank") BlankRequested?.Invoke(this, EventArgs.Empty);
                else if (type == "goto")
                {
                    if (doc.RootElement.TryGetProperty("index", out var idxEl))
                        GotoRequested?.Invoke(idxEl.GetInt32());
                }
                else if (type == "goto_song")
                {
                    if (doc.RootElement.TryGetProperty("index", out var idxEl))
                        GotoSongRequested?.Invoke(idxEl.GetInt32());
                }
                else if (type == "setlist_add")
                {
                    if (doc.RootElement.TryGetProperty("songId", out var idEl))
                        SetlistAddRequested?.Invoke(idEl.GetInt32());
                }
                else if (type == "setlist_remove")
                {
                    if (doc.RootElement.TryGetProperty("index", out var idxEl))
                        SetlistRemoveRequested?.Invoke(idxEl.GetInt32());
                }
                else if (type == "setlist_move")
                {
                    if (doc.RootElement.TryGetProperty("from", out var fromEl) &&
                        doc.RootElement.TryGetProperty("to", out var toEl))
                        SetlistMoveRequested?.Invoke(fromEl.GetInt32(), toEl.GetInt32());
                }
                else if (type == "get_songs")
                {
                    int offset = doc.RootElement.TryGetProperty("offset", out var offEl) ? offEl.GetInt32() : 0;
                    int limit  = doc.RootElement.TryGetProperty("limit",  out var limEl) ? limEl.GetInt32() : 100;
                    GetSongsRequested?.Invoke(ws, offset, limit);
                }
                else if (type == "sync_push")
                {
                    var rawJson = Encoding.UTF8.GetString(ms.ToArray());
                    SyncPushRequested?.Invoke(ws, rawJson);
                }
                else if (type == "setlist_clear")
                {
                    SetlistClearRequested?.Invoke();
                }
                else if (type == "setlist_restore")
                {
                    if (doc.RootElement.TryGetProperty("songs", out var songsEl))
                    {
                        var ids = songsEl.EnumerateArray()
                            .Select(s => s.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0)
                            .Where(id => id > 0)
                            .ToArray();
                        SetlistRestoreRequested?.Invoke(ids);
                    }
                }
                else if (type == "get_setlists")
                {
                    GetSetlistsRequested?.Invoke(ws);
                }
                else if (type == "open_setlist")
                {
                    if (doc.RootElement.TryGetProperty("id", out var idEl))
                        OpenSetlistRequested?.Invoke(ws, idEl.GetInt32());
                }
                else if (type == "get_setlist_detail")
                {
                    if (doc.RootElement.TryGetProperty("id", out var idEl))
                        GetSetlistDetailRequested?.Invoke(ws, idEl.GetInt32());
                }
                else if (type == "setlist_sync_push")
                {
                    var rawJson = Encoding.UTF8.GetString(ms.ToArray());
                    SetlistSyncPushRequested?.Invoke(ws, rawJson);
                }
                else if (type == "setlist_delete")
                {
                    if (doc.RootElement.TryGetProperty("desktopId", out var idEl) &&
                        idEl.ValueKind == JsonValueKind.Number)
                        SetlistDeleteRequested?.Invoke(ws, idEl.GetInt32());
                }
                else if (type == "devices_power_all")
                {
                    if (doc.RootElement.TryGetProperty("on", out var onEl))
                        DevicesPowerAllRequested?.Invoke(onEl.GetBoolean());
                }
            }
            catch { }
        }
    }

    // ─── Uwierzytelnianie: pomocnicze ───────────────────────────────────

    private bool TokenKnown(string token)
    {
        lock (_lock) return _tokens.Contains(token);
    }

    private bool PinMatches(string pin)
    {
        var expected = Pin ?? "";
        if (expected.Length == 0) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(pin), Encoding.UTF8.GetBytes(expected));
    }

    private string IssueToken()
    {
        var token = GenerateToken();
        lock (_lock)
        {
            _tokens.Add(token);
            while (_tokens.Count > MaxTokens) _tokens.Remove(_tokens.First());
        }
        TokenIssued?.Invoke(token);
        return token;
    }

    private Task SendAuthOkAsync(WebSocket ws, string token) =>
        SafeSendAsync(ws, JsonSerializer.Serialize(new { type = "auth_ok", token }));

    private static string AuthFailedJson(int retryAfter) =>
        JsonSerializer.Serialize(new { type = "auth_failed", retryAfter });

    private int LockRemainingSeconds(string ip)
    {
        lock (_lock)
        {
            if (!_ipFailures.TryGetValue(ip, out var f)) return 0;
            var left = f.LockedUntil - DateTime.UtcNow;
            return left > TimeSpan.Zero ? (int)Math.Ceiling(left.TotalSeconds) : 0;
        }
    }

    private void RegisterIpFailure(string ip)
    {
        lock (_lock)
        {
            if (!_ipFailures.TryGetValue(ip, out var f))
                _ipFailures[ip] = f = new IpFailure();
            // Licznik wygasa po okresie blokady bez nowych prób
            if (DateTime.UtcNow - f.LastFail > IpLockout) f.Count = 0;
            f.Count++;
            f.LastFail = DateTime.UtcNow;
            if (f.Count >= MaxIpFailures)
            {
                f.LockedUntil = DateTime.UtcNow + IpLockout;
                f.Count = 0;
            }
        }
    }

    private void ClearIpFailures(string ip)
    {
        lock (_lock) _ipFailures.Remove(ip);
    }

    private void Reject(string ip, string reason)
    {
        Debug.WriteLine($"[Pilot] Odrzucono urządzenie {ip}: {reason}");
        ClientRejected?.Invoke($"{ip} — {reason}");
    }

    private static async Task CloseRejectedAsync(WebSocket ws)
    {
        try
        {
            await ws.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation,
                "auth", CancellationToken.None);
        }
        catch { }
    }

    private static async Task SafeSendAsync(WebSocket ws, string json)
    {
        if (ws.State != WebSocketState.Open) return;
        try
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes(json),
                WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
    }

    private static string GetHtml() => """
        <!DOCTYPE html>
        <html lang="pl">
        <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
        <title>Cantio Pilot</title>
        <style>
          * { box-sizing: border-box; margin: 0; padding: 0; }
          body { background: #0f1117; color: #e8eaf0; font-family: 'Segoe UI', sans-serif;
                 display: flex; flex-direction: column; height: 100dvh; overflow: hidden; }
          #status { font-size: 12px; color: #666; text-align: center; padding: 6px 0; flex-shrink: 0; }
          #status.ok { color: #4caf50; }
          #song { font-size: 13px; color: #c9a84c; text-align: center;
                  padding: 4px 16px; flex-shrink: 0; min-height: 22px; }
          #slide { flex: 1; display: flex; align-items: center; justify-content: center;
                   padding: 20px; font-size: clamp(18px, 5vw, 32px);
                   text-align: center; line-height: 1.6; white-space: pre-wrap; overflow: auto; }
          #counter { font-size: 11px; color: #444; text-align: center;
                     padding: 4px 0; flex-shrink: 0; min-height: 18px; }
          #controls { display: flex; gap: 12px; padding: 16px; flex-shrink: 0; }
          .btn { flex: 1; height: 88px; font-size: 40px; background: #161b25;
                 border: 2px solid #c9a84c; border-radius: 14px; color: #c9a84c;
                 cursor: pointer; touch-action: manipulation; user-select: none; }
          .btn:active { background: #c9a84c22; }
          #auth { position: fixed; inset: 0; background: #0f1117; z-index: 10;
                  display: none; flex-direction: column; align-items: center;
                  justify-content: center; gap: 18px; padding: 24px; }
          #auth.show { display: flex; }
          #auth h1 { color: #c9a84c; font-size: 22px; font-weight: 600; }
          #auth p { color: #959fb9; font-size: 14px; text-align: center; }
          #pin { width: 220px; height: 70px; text-align: center; letter-spacing: 14px;
                 font-size: 38px; background: #161b25; color: #e8eaf0;
                 border: 2px solid #2a3347; border-radius: 14px; }
          #pin:focus { outline: none; border-color: #c9a84c; }
          #pinBtn { width: 220px; height: 56px; font-size: 18px; font-weight: 600;
                    background: #c9a84c; color: #0f1117; border: 0; border-radius: 14px;
                    cursor: pointer; touch-action: manipulation; }
          #pinErr { color: #e05555; font-size: 14px; min-height: 20px; text-align: center; }
        </style>
        </head>
        <body>
        <div id="auth">
          <h1>Cantio Pilot</h1>
          <p>Wpisz PIN parowania<br>(Cantio → USTAWIENIA → Pilot mobilny)</p>
          <input id="pin" type="tel" inputmode="numeric" maxlength="4" autocomplete="off">
          <button id="pinBtn" onclick="sendPin()">Połącz</button>
          <div id="pinErr"></div>
        </div>
        <div id="status">Łączenie...</div>
        <div id="song"></div>
        <div id="slide">—</div>
        <div id="counter"></div>
        <div id="controls">
          <button class="btn" onclick="send('prev')">&#8592;</button>
          <button class="btn" onclick="send('next')">&#8594;</button>
        </div>
        <script>
          let ws, timer, lastTry = null;
          const urlPin = new URLSearchParams(location.search).get('pin');
          const $ = id => document.getElementById(id);
          const showAuth = on => $('auth').classList.toggle('show', on);

          function connect() {
            ws = new WebSocket('ws://' + location.host + '/ws');
            ws.onopen = () => {
              $('status').textContent = 'Połączono ✓';
              $('status').className = 'ok';
              clearTimeout(timer);
            };
            ws.onmessage = e => {
              const d = JSON.parse(e.data);
              if (d.type === 'auth_required') {
                const token = localStorage.getItem('cantio_token');
                if (token) auth({token: token}, 'token');
                else if (urlPin) auth({pin: urlPin}, 'urlpin');
                else { showAuth(true); $('pin').focus(); }
              } else if (d.type === 'auth_ok') {
                if (d.token) localStorage.setItem('cantio_token', d.token);
                showAuth(false);
                $('pinErr').textContent = '';
              } else if (d.type === 'auth_failed') {
                if (d.retryAfter > 0) {
                  showAuth(true);
                  $('pinErr').textContent = 'Za dużo prób — odczekaj ' + d.retryAfter + ' s';
                } else if (lastTry === 'token') {
                  localStorage.removeItem('cantio_token');
                  if (urlPin) auth({pin: urlPin}, 'urlpin');
                  else { showAuth(true); $('pinErr').textContent = 'Urządzenie odparowane — wpisz PIN'; }
                } else {
                  showAuth(true);
                  $('pin').value = '';
                  $('pin').focus();
                  $('pinErr').textContent = 'Błędny PIN';
                }
              } else if (d.type === 'slide') {
                $('slide').textContent = d.text || '—';
                $('song').textContent = d.songTitle || '';
                $('counter').textContent =
                  d.total > 0 ? (d.index + 1) + ' / ' + d.total : '';
              }
            };
            ws.onclose = () => {
              $('status').textContent = 'Rozłączono — ponawianie...';
              $('status').className = '';
              timer = setTimeout(connect, 2000);
            };
            ws.onerror = () => ws.close();
          }
          function auth(payload, kind) {
            lastTry = kind;
            payload.type = 'auth';
            if (ws && ws.readyState === 1) ws.send(JSON.stringify(payload));
          }
          function sendPin() {
            const v = $('pin').value.trim();
            if (v.length < 4) { $('pinErr').textContent = 'PIN ma 4 cyfry'; return; }
            $('pinErr').textContent = '';
            auth({pin: v}, 'pin');
          }
          document.addEventListener('DOMContentLoaded', () => {
            $('pin').addEventListener('keydown', e => { if (e.key === 'Enter') sendPin(); });
            $('pin').addEventListener('input', e => {
              e.target.value = e.target.value.replace(/\D/g, '');
              if (e.target.value.length === 4) sendPin();
            });
          });
          function send(a) {
            if (ws && ws.readyState === 1) ws.send(JSON.stringify({type: a}));
          }
          connect();
        </script>
        </body>
        </html>
        """;

    public void Dispose() => Stop();
}
