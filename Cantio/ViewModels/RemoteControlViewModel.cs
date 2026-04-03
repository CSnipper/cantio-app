using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Media.Imaging;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;

namespace Cantio.ViewModels;

public partial class RemoteControlViewModel : ObservableObject
{
    private readonly RemoteControlServer _server = new();

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _localUrl = "";
    [ObservableProperty] private BitmapSource? _qrCode;
    [ObservableProperty] private int _port = 8765;

    public event EventHandler? NextRequested;
    public event EventHandler? PrevRequested;

    public RemoteControlViewModel()
    {
        _server.NextRequested += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
        _server.PrevRequested += (_, _) => PrevRequested?.Invoke(this, EventArgs.Empty);
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
            _server.Start(Port);
            IsRunning = true;
            var ip = GetLocalIp();
            LocalUrl = $"http://{ip}:{Port}";
            QrCode = GenerateQr(LocalUrl);
        }
    }

    public Task BroadcastAsync(string text, string songTitle, int index, int total)
        => _server.IsRunning
            ? _server.BroadcastAsync(text, songTitle, index, total)
            : Task.CompletedTask;

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

    private static BitmapSource GenerateQr(string url)
    {
        var data = new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
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
