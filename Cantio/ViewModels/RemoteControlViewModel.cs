using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media.Imaging;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;

namespace Cantio.ViewModels;

public partial class RemoteControlViewModel : ObservableObject, IDisposable
{
    private readonly RemoteControlServer _server = new();
    private readonly DatabaseService? _db;
    private bool _initializing;

    [ObservableProperty] private bool        _isRunning;
    [ObservableProperty] private string      _localUrl = "";
    [ObservableProperty] private BitmapSource? _qrCode;
    [ObservableProperty] private int         _port     = 8765;
    [ObservableProperty] private bool        _rememberState;
    [ObservableProperty] private string      _pin        = "";
    [ObservableProperty] private bool        _requirePin = true;
    [ObservableProperty] private string      _lastRejected = "";

    private List<string> _tokens = [];

    public event EventHandler? NextRequested;
    public event EventHandler? PrevRequested;
    public event EventHandler? BlankRequested;
    public event Action<int>? GotoRequested;
    public event Action<int>? GotoSongRequested;
    public event Action<int>? SetlistAddRequested;
    public event Action<int>? SetlistRemoveRequested;
    public event Action<int, int>? SetlistMoveRequested;
    public event Action<System.Net.WebSockets.WebSocket, int, int>? GetSongsRequested;
    public event Action<System.Net.WebSockets.WebSocket, string>? SyncPushRequested;
    public event Action? SetlistClearRequested;
    public event Action<int[], int>? SetlistRestoreRequested; // songIds, activeIndex (-1 = brak pola)
    public event Action<System.Net.WebSockets.WebSocket>? ClientConnected;
    public event Action<System.Net.WebSockets.WebSocket>? GetSetlistsRequested;
    public event Action<System.Net.WebSockets.WebSocket, int>? OpenSetlistRequested;
    public event Action<System.Net.WebSockets.WebSocket, int>? GetSetlistDetailRequested;
    public event Action<System.Net.WebSockets.WebSocket, string>? SetlistSyncPushRequested;
    public event Action<System.Net.WebSockets.WebSocket, int>? SetlistDeleteRequested;
    public event Action<bool>? DevicesPowerAllRequested;
    public event Action<System.Net.WebSockets.WebSocket>? StatusRequested;
    public event Action<System.Net.WebSockets.WebSocket>? RestartAppRequested;
    public event Action<System.Net.WebSockets.WebSocket, bool>? ProjectionRequested;

    /// <summary>
    /// Zmienił się stan parowania (start serwera, nowe urządzenie, „nowy PIN").
    /// Odpalane także z wątku serwera — subskrybent marshaluje na Dispatcher sam.
    /// </summary>
    public event Action? PairingStateChanged;

    public RemoteControlViewModel(DatabaseService? db = null)
    {
        _db = db;
        _server.NextRequested           += (_, _)         => NextRequested?.Invoke(this, EventArgs.Empty);
        _server.PrevRequested           += (_, _)         => PrevRequested?.Invoke(this, EventArgs.Empty);
        _server.BlankRequested          += (_, _)         => BlankRequested?.Invoke(this, EventArgs.Empty);
        _server.GotoRequested           += idx            => GotoRequested?.Invoke(idx);
        _server.GotoSongRequested       += idx            => GotoSongRequested?.Invoke(idx);
        _server.SetlistAddRequested     += id             => SetlistAddRequested?.Invoke(id);
        _server.SetlistRemoveRequested  += idx            => SetlistRemoveRequested?.Invoke(idx);
        _server.SetlistMoveRequested    += (f, t)         => SetlistMoveRequested?.Invoke(f, t);
        _server.GetSongsRequested       += (ws, off, lim) => GetSongsRequested?.Invoke(ws, off, lim);
        _server.SyncPushRequested       += (ws, json)     => SyncPushRequested?.Invoke(ws, json);
        _server.SetlistClearRequested   += ()             => SetlistClearRequested?.Invoke();
        _server.SetlistRestoreRequested += (ids, active)  => SetlistRestoreRequested?.Invoke(ids, active);
        _server.ClientConnected         += ws             => ClientConnected?.Invoke(ws);
        _server.GetSetlistsRequested        += ws         => GetSetlistsRequested?.Invoke(ws);
        _server.OpenSetlistRequested        += (ws, id)   => OpenSetlistRequested?.Invoke(ws, id);
        _server.GetSetlistDetailRequested   += (ws, id)   => GetSetlistDetailRequested?.Invoke(ws, id);
        _server.SetlistSyncPushRequested    += (ws, json) => SetlistSyncPushRequested?.Invoke(ws, json);
        _server.SetlistDeleteRequested      += (ws, id)   => SetlistDeleteRequested?.Invoke(ws, id);
        _server.DevicesPowerAllRequested    += on         => DevicesPowerAllRequested?.Invoke(on);
        _server.StatusRequested             += ws         => StatusRequested?.Invoke(ws);
        _server.RestartAppRequested         += ws         => RestartAppRequested?.Invoke(ws);
        _server.ProjectionRequested         += (ws, open) => ProjectionRequested?.Invoke(ws, open);
        _server.TokenIssued                 += OnTokenIssued;
        _server.ClientRejected              += info =>
        {
            var msg = $"{DateTime.Now:HH:mm} — {info}";
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp != null) disp.Invoke(() => LastRejected = msg);
            else LastRejected = msg;
        };
    }

    private void OnTokenIssued(string token)
    {
        lock (_tokens)
        {
            if (_tokens.Contains(token)) return;
            _tokens.Add(token);
            while (_tokens.Count > RemoteControlServer.MaxTokens) _tokens.RemoveAt(0);
        }
        SaveTokens();
        PairingStateChanged?.Invoke();   // pierwsze parowanie gasi ekran startowy na projektorze
    }

    /// <summary>Liczba sparowanych urządzeń — wejście reguły ekranu parowania.</summary>
    public int PairedDeviceCount { get { lock (_tokens) return _tokens.Count; } }

    /// <summary>Czy na projektorze ma wisieć ekran parowania (tryb serwerowy + nikt niesparowany).</summary>
    public bool ShouldShowPairingScreen =>
        IsRunning && AppModeRules.ShouldShowPairingScreen(AppMode.Current, PairedDeviceCount);

    /// <summary>
    /// WSZYSTKIE adresy IPv4, pod którymi widać serwer. Mini PC bywa w sieci przewodowej
    /// i Wi-Fi naraz — zgadywanie jednego adresu (jak <see cref="LocalUrl"/>) zostawiłoby
    /// parafię z adresem, którego tablet nie widzi.
    /// </summary>
    public IReadOnlyList<string> AllLocalUrls =>
        [.. AllLocalIps().Select(ip => $"http://{ip}:{Port}")];

    /// <summary>Kod QR w rozmiarze projektorowym (ta sama treść co w panelu pilota).</summary>
    public BitmapSource? PairingQrLarge =>
        IsRunning ? GenerateQr(PairingUrl, pixelsPerModule: 20) : null;

    private void SaveTokens()
    {
        if (_db == null) return;
        string json;
        lock (_tokens) json = System.Text.Json.JsonSerializer.Serialize(_tokens);
        _ = _db.SaveSettingAsync("pilot_tokens", json);
    }

    [RelayCommand]
    private void ToggleServer()
    {
        if (_server.IsRunning)
        {
            _server.Stop();
            IsRunning = false;
            LocalUrl = "";
            QrCode = null;
        }
        else
        {
            ApplyAuthConfig();
            try
            {
                _server.Start(Port);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or System.Net.Sockets.SocketException)
            {
                // Port invalid or already in use — do not set IsRunning
                return;
            }
            IsRunning = true;
            LastRejected = "";
            var ip = GetLocalIp();
            LocalUrl = $"http://{ip}:{Port}";
            QrCode = GenerateQr(PairingUrl);
        }
        PersistState();
        PairingStateChanged?.Invoke();
    }

    /// <summary>Adres w kodzie QR — z PIN-em, żeby skanowanie parowało bez przepisywania.</summary>
    public string PairingUrl =>
        RequirePin && Pin.Length == 4 ? $"{LocalUrl}/?pin={Pin}" : LocalUrl;

    /// <summary>Przenosi PIN/tokeny/tryb do serwera; generuje PIN gdy go brak.</summary>
    private void ApplyAuthConfig()
    {
        if (string.IsNullOrEmpty(Pin))
        {
            Pin = RemoteControlServer.GeneratePin();
            _ = _db?.SaveSettingAsync("pilot_pin", Pin);
        }
        _server.RequirePin = RequirePin;
        _server.Pin = Pin;
        lock (_tokens) _server.SetTokens(_tokens);
    }

    /// <summary>Nowy PIN — unieważnia wszystkie sparowane urządzenia.</summary>
    [RelayCommand]
    private void NewPin()
    {
        Pin = RemoteControlServer.GeneratePin();
        lock (_tokens) _tokens.Clear();
        _server.ClearTokens();
        _server.Pin = Pin;
        if (_db != null) _ = _db.SaveSettingAsync("pilot_pin", Pin);
        SaveTokens();
        _server.DisconnectAllClients();   // stare sesje też tracą ważność
        RefreshQr();
    }

    private void RefreshQr()
    {
        OnPropertyChanged(nameof(PairingUrl));
        if (IsRunning) QrCode = GenerateQr(PairingUrl);
        PairingStateChanged?.Invoke();   // „nowy PIN" kasuje tokeny → ekran parowania wraca
    }

    partial void OnRequirePinChanged(bool value)
    {
        _server.RequirePin = value;
        if (_initializing || _db == null) return;
        _ = _db.SaveSettingAsync("pilot_require_pin", value ? "1" : "0");
        if (value) _server.DisconnectAllClients();   // wymuś parowanie już podłączonych
        RefreshQr();
    }

    /// <summary>Wczytuje zapamiętany stan i auto-startuje serwer, jeśli działał przy zamknięciu.</summary>
    public async Task InitAsync()
    {
        if (_db == null) return;
        _initializing = true;
        try
        {
            RememberState = await _db.GetSettingAsync("pilot_remember") == "1";
            if (int.TryParse(await _db.GetSettingAsync("pilot_port"), out var port)) Port = port;

            RequirePin = await _db.GetSettingAsync("pilot_require_pin") != "0";  // domyślnie włączone
            Pin = await _db.GetSettingAsync("pilot_pin") ?? "";
            if (Pin.Length != 4)
            {
                Pin = RemoteControlServer.GeneratePin();
                await _db.SaveSettingAsync("pilot_pin", Pin);
            }
            var tokensJson = await _db.GetSettingAsync("pilot_tokens");
            if (!string.IsNullOrWhiteSpace(tokensJson))
            {
                try
                {
                    _tokens = System.Text.Json.JsonSerializer
                        .Deserialize<List<string>>(tokensJson) ?? [];
                }
                catch { _tokens = []; }
            }
            ApplyAuthConfig();

            // Tryb serwerowy: pilot jest jedynym interfejsem, więc startuje zawsze.
            var wasRunning = await _db.GetSettingAsync("pilot_was_running") == "1";
            if (!IsRunning &&
                AppModeRules.ShouldAutoStartPilotServer(AppMode.Current, RememberState, wasRunning))
                ToggleServer();
        }
        finally { _initializing = false; }
        PairingStateChanged?.Invoke();
    }

    partial void OnPinChanged(string value) => OnPropertyChanged(nameof(PairingUrl));
    partial void OnLocalUrlChanged(string value) => OnPropertyChanged(nameof(PairingUrl));

    partial void OnRememberStateChanged(bool value)
    {
        if (_initializing || _db == null) return;
        _ = _db.SaveSettingAsync("pilot_remember", value ? "1" : "0");
        PersistState();
    }

    private void PersistState()
    {
        if (_initializing || _db == null) return;
        _ = _db.SaveSettingAsync("pilot_was_running", IsRunning ? "1" : "0");
        _ = _db.SaveSettingAsync("pilot_port", Port.ToString());
    }

    [RelayCommand]
    private static void OpenHotspotSettings() => HotspotService.OpenSettings();

    public Task BroadcastAsync(
        string text, string songTitle, int index, int total,
        bool isBlank = false, IList<string>? slides = null,
        IList<string>? slideKinds = null, string? kind = null)
        => _server.IsRunning
            ? _server.BroadcastAsync(text, songTitle, index, total, isBlank, slides, slideKinds, kind)
            : Task.CompletedTask;

    public Task BroadcastSetlistAsync(IList<(int id, string title)> songs, int activeIndex)
        => _server.IsRunning
            ? _server.BroadcastSetlistAsync(songs, activeIndex)
            : Task.CompletedTask;

    public Task BroadcastDevicesAsync(string state, int count)
        => _server.IsRunning
            ? _server.BroadcastDevicesAsync(state, count)
            : Task.CompletedTask;

    public Task SendToClientAsync(System.Net.WebSockets.WebSocket ws, string json)
        => _server.SendToClientAsync(ws, json);

    /// <summary>Adresy IPv4 wszystkich działających interfejsów (bez loopbacku), bez duplikatów.</summary>
    private static List<string> AllLocalIps()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var s = ua.Address.ToString();
                    if (s.StartsWith("169.254.")) continue;   // APIPA — brak realnej sieci
                    if (!result.Contains(s)) result.Add(s);
                }
            }
        }
        catch { }
        if (result.Count == 0)
        {
            var fallback = GetLocalIp();
            if (fallback != "localhost") result.Add(fallback);
        }
        return result;
    }

    private static string GetLocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 80);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch { return "localhost"; }
    }

    public void Dispose() => _server.Dispose();

    private static BitmapSource GenerateQr(string url, int pixelsPerModule = 8)
    {
        using var qrGenerator = new QRCodeGenerator();
        var data = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule,
            new byte[] { 201, 168, 76 },   // gold
            new byte[] { 15, 17, 23 });    // dark bg
        var img = new BitmapImage();
        using var ms = new MemoryStream(png);
        img.BeginInit();
        img.StreamSource = ms;
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
