using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

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

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        IsCheckingUpdate = true;
        IsUpdateAvailable = false;
        try
        {
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Cantio/" + AppVersion);
            var json = await _http.GetFromJsonAsync<JsonElement>(
                "https://api.github.com/repos/CSnipper/cantio-app/releases/latest");
            LatestVersion = json.GetProperty("tag_name").GetString()?.TrimStart('v') ?? string.Empty;
            IsUpdateAvailable = !string.IsNullOrEmpty(LatestVersion) && LatestVersion != AppVersion;
        }
        catch { /* brak internetu lub błąd API — nic nie pokazuj */ }
        finally { IsCheckingUpdate = false; }
    }

    [RelayCommand]
    private static void OpenWebsite() =>
        Process.Start(new ProcessStartInfo("https://cantio.app") { UseShellExecute = true });

    [RelayCommand]
    private static void OpenLatestRelease() =>
        Process.Start(new ProcessStartInfo("https://github.com/CSnipper/cantio-app/releases/latest")
            { UseShellExecute = true });
}
