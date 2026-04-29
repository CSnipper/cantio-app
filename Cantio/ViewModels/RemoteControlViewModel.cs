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

    [ObservableProperty] private bool        _isRunning;
    [ObservableProperty] private string      _localUrl = "";
    [ObservableProperty] private BitmapSource? _qrCode;
    [ObservableProperty] private int         _port     = 8765;

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
    public event Action<int[]>? SetlistRestoreRequested;
    public event Action<System.Net.WebSockets.WebSocket>? ClientConnected;

    public RemoteControlViewModel()
    {
        _server.NextRequested          += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
        _server.PrevRequested          += (_, _) => PrevRequested?.Invoke(this, EventArgs.Empty);
        _server.BlankRequested         += (_, _) => BlankRequested?.Invoke(this, EventArgs.Empty);
        _server.GotoRequested          += idx    => GotoRequested?.Invoke(idx);
        _server.GotoSongRequested      += idx    => GotoSongRequested?.Invoke(idx);
        _server.SetlistAddRequested    += id     => SetlistAddRequested?.Invoke(id);
        _server.SetlistRemoveRequested += idx    => SetlistRemoveRequested?.Invoke(idx);
        _server.SetlistMoveRequested   += (f, t) => SetlistMoveRequested?.Invoke(f, t);
        _server.GetSongsRequested      += (ws, off, lim) => GetSongsRequested?.Invoke(ws, off, lim);
        _server.SyncPushRequested       += (ws, json) => SyncPushRequested?.Invoke(ws, json);
        _server.SetlistClearRequested   += ()         => SetlistClearRequested?.Invoke();
        _server.SetlistRestoreRequested += ids        => SetlistRestoreRequested?.Invoke(ids);
        _server.ClientConnected         += ws         => ClientConnected?.Invoke(ws);
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
            var ip = GetLocalIp();
            LocalUrl = $"http://{ip}:{Port}";
            QrCode = GenerateQr(LocalUrl);
        }
    }

    [RelayCommand]
    private static void OpenHotspotSettings() => HotspotService.OpenSettings();

    public Task BroadcastAsync(
        string text, string songTitle, int index, int total,
        bool isBlank = false, IList<string>? slides = null)
        => _server.IsRunning
            ? _server.BroadcastAsync(text, songTitle, index, total, isBlank, slides)
            : Task.CompletedTask;

    public Task BroadcastSetlistAsync(IList<(int id, string title)> songs, int activeIndex)
        => _server.IsRunning
            ? _server.BroadcastSetlistAsync(songs, activeIndex)
            : Task.CompletedTask;

    public Task SendToClientAsync(System.Net.WebSockets.WebSocket ws, string json)
        => _server.SendToClientAsync(ws, json);

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

    private static BitmapSource GenerateQr(string url)
    {
        using var qrGenerator = new QRCodeGenerator();
        var data = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8,
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
