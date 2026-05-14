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

    public void Stop()
    {
        _cts.Cancel();
        _listener?.Stop();
        _listener = null;
        _udpDiscovery?.Dispose();
        _udpDiscovery = null;
        IsRunning = false;
        List<WebSocket> toClose;
        lock (_lock) { toClose = [.. _clients]; _clients.Clear(); }
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
                lock (_lock) _clients.Add(ws);
                ClientConnected?.Invoke(ws);
                try { await ReceiveLoopAsync(ws, ct); }
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

    private async Task ReceiveLoopAsync(WebSocket ws, CancellationToken ct)
    {
        var buf = new byte[1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await ws.ReceiveAsync(buf, ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buf, 0, result.Count);
                } while (!result.EndOfMessage);
            }
            catch { break; }
            try
            {
                var doc = JsonDocument.Parse(ms.ToArray());
                var type = doc.RootElement.GetProperty("type").GetString();
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
            }
            catch { }
        }
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
        </style>
        </head>
        <body>
        <div id="status">Łączenie...</div>
        <div id="song"></div>
        <div id="slide">—</div>
        <div id="counter"></div>
        <div id="controls">
          <button class="btn" onclick="send('prev')">&#8592;</button>
          <button class="btn" onclick="send('next')">&#8594;</button>
        </div>
        <script>
          let ws, timer;
          function connect() {
            ws = new WebSocket('ws://' + location.host + '/ws');
            ws.onopen = () => {
              document.getElementById('status').textContent = 'Połączono ✓';
              document.getElementById('status').className = 'ok';
              clearTimeout(timer);
            };
            ws.onmessage = e => {
              const d = JSON.parse(e.data);
              if (d.type === 'slide') {
                document.getElementById('slide').textContent = d.text || '—';
                document.getElementById('song').textContent = d.songTitle || '';
                document.getElementById('counter').textContent =
                  d.total > 0 ? (d.index + 1) + ' / ' + d.total : '';
              }
            };
            ws.onclose = () => {
              document.getElementById('status').textContent = 'Rozłączono — ponawianie...';
              document.getElementById('status').className = '';
              timer = setTimeout(connect, 2000);
            };
            ws.onerror = () => ws.close();
          }
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
