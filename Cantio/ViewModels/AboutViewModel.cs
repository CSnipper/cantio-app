using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
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
            var result = await FetchLatestReleaseAsync();
            if (result is not null)
            {
                LatestVersion  = result.Value.Version;
                DownloadUrl    = result.Value.DownloadUrl;
                ReleaseNotes   = result.Value.ReleaseNotes;
                IsUpdateAvailable = true;
            }
        }
        finally { IsCheckingUpdate = false; }
    }

    private bool _isPromptActive;

    public async Task CheckAndPromptAsync()
    {
        if (_isPromptActive) return;
        _isPromptActive = true;
        try
        {
            var result = await FetchLatestReleaseAsync();
            if (result is null) return;

            LatestVersion     = result.Value.Version;
            DownloadUrl       = result.Value.DownloadUrl;
            ReleaseNotes      = result.Value.ReleaseNotes;
            IsUpdateAvailable = true;

            // Tryb serwerowy: brak operatora, więc pytanie „zainstalować?" zawiesiłoby aplikację
            // na zawsze. Aktualizacji tu NIE automatyzujemy — zostaje ślad w logu.
            if (!AppModeRules.CanShowBlockingDialog(AppMode.Current))
            {
                AppLog.Write("Update", $"Dostępna nowa wersja {LatestVersion} — pominięto pytanie (tryb serwerowy).");
                return;
            }

            var choice = MessageBox.Show(
                $"Dostępna jest nowa wersja Cantio {LatestVersion}.\n\nCzy chcesz ją teraz zainstalować?",
                "Aktualizacja", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (choice == MessageBoxResult.Yes)
                await DownloadAndInstallAsync();
        }
        finally { _isPromptActive = false; }
    }

    private async Task<(string Version, string DownloadUrl, string ReleaseNotes)?> FetchLatestReleaseAsync()
    {
        try
        {
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Cantio/" + AppVersion);
            var json = await _http.GetFromJsonAsync<JsonElement>(
                "https://api.github.com/repos/CSnipper/cantio-app/releases/latest");

            var latest = json.GetProperty("tag_name").GetString()?.TrimStart('v') ?? string.Empty;
            if (string.IsNullOrEmpty(latest) || latest == AppVersion) return null;

            var notes = json.TryGetProperty("body", out var body)
                ? body.GetString() ?? string.Empty : string.Empty;

            string downloadUrl = string.Empty;
            if (json.TryGetProperty("assets", out var assets))
            {
                bool isX86 = RuntimeInformation.ProcessArchitecture == Architecture.X86;
                string archSuffix = isX86 ? "-x86.exe" : ".exe";

                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(archSuffix, StringComparison.OrdinalIgnoreCase)
                        && (isX86 || !name.EndsWith("-x86.exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        break;
                    }
                }
                if (string.IsNullOrEmpty(downloadUrl))
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
            }

            return (latest, downloadUrl, notes);
        }
        catch { return null; }
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

    [RelayCommand]
    private static void OpenGitHub() =>
        Process.Start(new ProcessStartInfo("https://github.com/CSnipper/cantio-app/") { UseShellExecute = true });
}
