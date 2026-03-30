using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace Cantio.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private static readonly HttpClient _http = new();

    public string AppVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}" : "?";
        }
    }

    [ObservableProperty] private bool _isCheckingUpdate = false;
    [ObservableProperty] private bool _isUpdateAvailable = false;
    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private string _releaseNotes = string.Empty;
    [ObservableProperty] private string _downloadUrl = string.Empty;
    [ObservableProperty] private bool _isDownloading = false;
    [ObservableProperty] private int _downloadProgress = 0;

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        IsCheckingUpdate = true;
        IsUpdateAvailable = false;
        ReleaseNotes = string.Empty;
        DownloadUrl = string.Empty;
        try
        {
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Cantio/" + AppVersion);
            var json = await _http.GetFromJsonAsync<JsonElement>(
                "https://api.github.com/repos/CSnipper/cantio-app/releases/latest");
            LatestVersion = json.GetProperty("tag_name").GetString()?.TrimStart('v') ?? string.Empty;
            IsUpdateAvailable = !string.IsNullOrEmpty(LatestVersion) && LatestVersion != AppVersion;

            if (IsUpdateAvailable)
            {
                ReleaseNotes = json.TryGetProperty("body", out var body)
                    ? body.GetString() ?? string.Empty : string.Empty;

                if (json.TryGetProperty("assets", out var assets))
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
            }
        }
        catch { /* brak internetu lub błąd API — nic nie pokazuj */ }
        finally { IsCheckingUpdate = false; }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAndInstallAsync()
    {
        IsDownloading = true;
        DownloadProgress = 0;
        var tempFile = Path.Combine(Path.GetTempPath(), $"CantioSetup-{LatestVersion}.exe");
        try
        {
            using var response = await _http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var fs = File.OpenWrite(tempFile);
            await using var stream = await response.Content.ReadAsStreamAsync();
            var buf = new byte[65536]; long downloaded = 0; int read;
            while ((read = await stream.ReadAsync(buf)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, read));
                downloaded += read;
                if (total > 0) DownloadProgress = (int)(downloaded * 100 / total);
            }
        }
        catch { IsDownloading = false; return; }
        Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private bool CanDownload() => !IsDownloading && !string.IsNullOrEmpty(DownloadUrl);

    partial void OnIsDownloadingChanged(bool value) => DownloadAndInstallCommand.NotifyCanExecuteChanged();
    partial void OnDownloadUrlChanged(string value) => DownloadAndInstallCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private static void OpenWebsite() =>
        Process.Start(new ProcessStartInfo("https://cantio.app") { UseShellExecute = true });
}
