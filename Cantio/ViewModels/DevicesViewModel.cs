using System.Collections.ObjectModel;
using System.Linq;
using Cantio.Helpers;
using Cantio.Models;
using Cantio.Services;
using Cantio.Services.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cantio.ViewModels;

/// <summary>
/// Wrapper na <see cref="ProjectionDevice"/> z UI-state: stan zasilania, zajętość,
/// edycja inline nazwy.
/// </summary>
public partial class DeviceItemViewModel : ObservableObject
{
    public ProjectionDevice Device { get; }

    [ObservableProperty] private DevicePowerState _state = DevicePowerState.Unknown;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editLabel = "";

    /// <summary>Numer 1-based pokazywany na przycisku paska górnego; przeliczany przez VM.</summary>
    [ObservableProperty] private int _number;

    public DeviceItemViewModel(ProjectionDevice device)
    {
        Device = device;
        _editName = device.Name;
        _editLabel = device.Label;
    }

    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(BarLabel));

    public string Name => Device.Name;
    public string Type => Device.Type;

    /// <summary>Krótkie oznaczenie użytkownika (maks. 4 znaki); puste = brak.</summary>
    public string Label => Device.Label;

    public bool HasLabel => !string.IsNullOrWhiteSpace(Device.Label);

    /// <summary>Napis na przycisku paska górnego: oznaczenie, a gdy puste — numer porządkowy.</summary>
    public string BarLabel => string.IsNullOrWhiteSpace(Device.Label)
        ? Number.ToString()
        : Device.Label;

    /// <summary>Czytelna etykieta typu do UI.</summary>
    public string TypeLabel => Device.Type switch
    {
        "samsung" => "Samsung TV",
        "pjlink" => "PJLink",
        "sony" => "Sony Bravia",
        _ => "Wake-on-LAN"
    };

    /// <summary>Odświeża widok po zmianie nazwy/oznaczenia w modelu.</summary>
    public void RefreshName()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(HasLabel));
        OnPropertyChanged(nameof(BarLabel));
        EditName = Device.Name;
        EditLabel = Device.Label;
    }
}

/// <summary>Wynik wykrywania telewizora Samsung (dla listy „Sparuj i dodaj").</summary>
public sealed class DiscoveredTv
{
    public string Name { get; init; } = "";
    public string Ip { get; init; } = "";
    public string Mac { get; init; } = "";
}

/// <summary>
/// ViewModel sterowania urządzeniami projekcyjnymi (TV/projektory).
/// Nie dotyka bazy bezpośrednio — cała warstwa przez <see cref="DeviceControlService"/>.
/// Operacje sieciowe są opakowane w try/catch — błąd = status tekstowy, nigdy crash.
/// </summary>
public partial class DevicesViewModel : ObservableObject
{
    private readonly DeviceControlService _control;
    private readonly SamsungTvDriver _samsung = new();
    private readonly PjLinkDriver _pjlink = new();
    private readonly SonyBraviaDriver _sony = new();

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];
    public ObservableCollection<DiscoveredTv> Discovered { get; } = [];

    /// <summary>Zmiana listy lub stanu zasilania urządzeń — sygnał do rozgłoszenia pilotom.</summary>
    public event Action? DevicesChanged;

    [ObservableProperty] private bool _hasDevices;

    /// <summary>
    /// Stan zbiorczy dla przycisku ⏻ na pasku: <c>On</c> = wszystkie włączone,
    /// <c>Unknown</c> = część włączona („mixed"), <c>Off</c> = nic nie działa.
    /// </summary>
    [ObservableProperty] private DevicePowerState _aggregatePowerState = DevicePowerState.Off;

    // ─── Formularz dodawania ─────────────────────────────────────────────
    [ObservableProperty] private string _addType = "samsung";
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newLabel = "";
    [ObservableProperty] private string _newIp = "";
    [ObservableProperty] private string _newMac = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _newPort = "4352";

    [ObservableProperty] private string _discoverStatus = "";
    [ObservableProperty] private bool _isDiscovering;
    [ObservableProperty] private string _pairStatus = "";
    [ObservableProperty] private string _testResult = "";
    [ObservableProperty] private string _addError = "";

    public bool IsSamsung => AddType == "samsung";
    public bool IsPjlink => AddType == "pjlink";
    public bool IsSony => AddType == "sony";
    public bool IsWol => AddType == "wol";

    public DevicesViewModel(DeviceControlService control) => _control = control;

    /// <summary>Normalizuje oznaczenie: trim + maks. 4 znaki (UI ma MaxLength, to zabezpieczenie modelu).</summary>
    private static string TrimLabel(string? value)
    {
        var s = (value ?? "").Trim();
        return s.Length > 4 ? s[..4] : s;
    }

    partial void OnAddTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSamsung));
        OnPropertyChanged(nameof(IsPjlink));
        OnPropertyChanged(nameof(IsSony));
        OnPropertyChanged(nameof(IsWol));
        AddError = "";
        TestResult = "";
        PairStatus = "";
    }

    public async Task InitAsync()
    {
        await ReloadDevicesAsync();
        await RefreshStatesInternalAsync();
    }

    private async Task ReloadDevicesAsync()
    {
        var devices = await _control.GetDevicesAsync();
        Devices.Clear();
        foreach (var d in devices)
            Devices.Add(new DeviceItemViewModel(d));
        HasDevices = Devices.Count > 0;
        NotifyDevicesChanged();
    }

    private async Task PersistAsync()
    {
        await _control.SaveDevicesAsync(Devices.Select(i => i.Device).ToList());
        HasDevices = Devices.Count > 0;
        NotifyDevicesChanged();
    }

    /// <summary>
    /// Jedyna ścieżka powiadamiania o zmianie listy/stanu: przelicza numerację 1-based,
    /// odświeża stan zbiorczy i rozgłasza zmianę pilotom.
    /// </summary>
    private void NotifyDevicesChanged()
    {
        for (int i = 0; i < Devices.Count; i++)
            Devices[i].Number = i + 1;

        var (state, count) = GetAggregateState();
        AggregatePowerState = count == 0 ? DevicePowerState.Off : state switch
        {
            "on" => DevicePowerState.On,
            "mixed" => DevicePowerState.Unknown,
            _ => DevicePowerState.Off
        };

        DevicesChanged?.Invoke();
    }

    /// <summary>
    /// Zbiorczy stan urządzeń dla pilota: <c>state</c> ∈ <c>on|off|mixed</c>, <c>count</c> = liczba urządzeń.
    /// Cokolwiek włączone → traktowane jako „on/mixed" (przycisk pilota pokaże „wyłącz").
    /// </summary>
    public (string state, int count) GetAggregateState()
    {
        int count = Devices.Count;
        if (count == 0) return ("off", 0);
        int onCount = Devices.Count(d => d.State == DevicePowerState.On);
        string state = onCount == 0 ? "off" : onCount == count ? "on" : "mixed";
        return (state, count);
    }

    /// <summary>Włącza/wyłącza wszystkie urządzenia na żądanie pilota; po zmianie odświeża stany.</summary>
    public async Task SetAllPowerFromRemoteAsync(bool on)
    {
        try { await _control.PowerAllAsync(on); } catch { }
        await RefreshStatesInternalAsync();
    }

    // ─── Stany ───────────────────────────────────────────────────────────

    [RelayCommand]
    private Task RefreshStates() => RefreshStatesInternalAsync();

    private async Task RefreshStatesInternalAsync()
    {
        var tasks = Devices.Select(async item =>
        {
            try { item.State = await _control.GetStateAsync(item.Device); }
            catch { /* stan pozostaje jak był */ }
        });
        await Task.WhenAll(tasks);
        NotifyDevicesChanged();
    }

    // ─── Włączanie/wyłączanie ────────────────────────────────────────────

    [RelayCommand]
    private async Task PowerOn(DeviceItemViewModel? item)
    {
        if (item is null) return;
        item.IsBusy = true;
        try
        {
            await _control.PowerOnAsync(item.Device);
            item.State = await _control.GetStateAsync(item.Device);
        }
        catch { }
        finally { item.IsBusy = false; NotifyDevicesChanged(); }
    }

    [RelayCommand]
    private async Task PowerOff(DeviceItemViewModel? item)
    {
        if (item is null) return;
        item.IsBusy = true;
        try
        {
            await _control.PowerOffAsync(item.Device);
            item.State = await _control.GetStateAsync(item.Device);
        }
        catch { }
        finally { item.IsBusy = false; NotifyDevicesChanged(); }
    }

    [RelayCommand]
    private async Task PowerAllOn()
    {
        try { await _control.PowerAllAsync(true); } catch { }
        await RefreshStatesInternalAsync();
    }

    [RelayCommand]
    private async Task PowerAllOff()
    {
        try { await _control.PowerAllAsync(false); } catch { }
        await RefreshStatesInternalAsync();
    }

    // ─── Usuwanie / zmiana nazwy ─────────────────────────────────────────

    [RelayCommand]
    private async Task RemoveDevice(DeviceItemViewModel? item)
    {
        if (item is null) return;
        Devices.Remove(item);
        await PersistAsync();
    }

    [RelayCommand]
    private static void StartRename(DeviceItemViewModel? item)
    {
        if (item is null) return;
        item.EditName = item.Device.Name;
        item.EditLabel = item.Device.Label;
        item.IsEditing = true;
    }

    /// <summary>Zatwierdza edycję nazwy i krótkiego oznaczenia (oznaczenie może być puste).</summary>
    [RelayCommand]
    private async Task CommitRename(DeviceItemViewModel? item)
    {
        if (item is null) return;
        var name = (item.EditName ?? "").Trim();
        if (name.Length > 0) item.Device.Name = name;
        item.Device.Label = TrimLabel(item.EditLabel);
        item.IsEditing = false;
        item.RefreshName();
        await PersistAsync();
    }

    // ─── Pasek górny: przyciski numerowane + zbiorczy ────────────────────

    /// <summary>Przełącza jedno urządzenie: włączone → wyłącz, w innym razie (Off/Unknown) → włącz.</summary>
    [RelayCommand]
    private Task ToggleDevice(DeviceItemViewModel? item)
    {
        if (item is null) return Task.CompletedTask;
        return item.State == DevicePowerState.On ? PowerOff(item) : PowerOn(item);
    }

    /// <summary>
    /// Przycisk ⏻: cokolwiek włączone (on/mixed) → wyłącz wszystkie; nic → włącz wszystkie.
    /// Ta sama reguła co przycisk zasilania w pilocie Android.
    /// </summary>
    [RelayCommand]
    private Task TogglePowerAll()
    {
        var (state, count) = GetAggregateState();
        if (count == 0) return Task.CompletedTask;
        return state == "off" ? PowerAllOn() : PowerAllOff();
    }

    // ─── Wykrywanie / parowanie Samsung ──────────────────────────────────

    [RelayCommand]
    private async Task Discover()
    {
        IsDiscovering = true;
        Discovered.Clear();
        DiscoverStatus = LocalizationManager.Get("Devices.Discovering");
        try
        {
            var found = await SamsungTvDriver.DiscoverAsync(TimeSpan.FromSeconds(4));
            foreach (var (name, ip, mac) in found)
                Discovered.Add(new DiscoveredTv { Name = name, Ip = ip, Mac = mac });
            DiscoverStatus = Discovered.Count == 0
                ? LocalizationManager.Get("Devices.DiscoverNone")
                : LocalizationManager.Format("Devices.DiscoverFound", Discovered.Count);
        }
        catch
        {
            DiscoverStatus = LocalizationManager.Get("Devices.DiscoverNone");
        }
        finally { IsDiscovering = false; }
    }

    [RelayCommand]
    private async Task PairAndAdd(DiscoveredTv? tv)
    {
        if (tv is null) return;
        var device = new ProjectionDevice
        {
            Type = "samsung",
            Name = string.IsNullOrWhiteSpace(tv.Name) ? tv.Ip : tv.Name,
            Label = TrimLabel(NewLabel),
            Ip = tv.Ip,
            Mac = tv.Mac
        };
        if (await PairAndAddDeviceAsync(device))
        {
            Discovered.Remove(tv);
            NewLabel = ""; // żeby to samo oznaczenie nie trafiło na kolejne urządzenie
        }
    }

    /// <summary>Ręczne dodanie Samsunga po IP — omija SSDP (dla sieci blokujących multicast).</summary>
    [RelayCommand]
    private async Task PairManual()
    {
        var ip = NewIp.Trim();
        if (ip.Length == 0)
        {
            PairStatus = LocalizationManager.Get("Devices.ErrIp");
            return;
        }

        PairStatus = LocalizationManager.Get("Devices.Connecting");
        var (name, mac) = await SamsungTvDriver.GetInfoAsync(ip);
        if (name is null)
        {
            PairStatus = LocalizationManager.Get("Devices.ErrNoConnection");
            return;
        }

        var newName = NewName.Trim();
        var device = new ProjectionDevice
        {
            Type = "samsung",
            Name = newName.Length > 0 ? newName : (name.Length > 0 ? name : ip),
            Label = TrimLabel(NewLabel),
            Ip = ip,
            Mac = mac ?? ""
        };
        if (await PairAndAddDeviceAsync(device))
            ClearForm();
    }

    /// <summary>
    /// Wspólna logika parowania (WS handshake) + dodania do listy + zapisu.
    /// Zwraca true jeśli sparowano i zapisano.
    /// </summary>
    private async Task<bool> PairAndAddDeviceAsync(ProjectionDevice device)
    {
        PairStatus = LocalizationManager.Get("Devices.PairConfirmOnTv");
        try
        {
            var (ok, token, error) = await _samsung.PairAsync(device);
            if (!ok)
            {
                PairStatus = string.IsNullOrEmpty(error)
                    ? LocalizationManager.Get("Devices.PairFailed")
                    : error;
                return false;
            }
            device.Token = token ?? "";
            Devices.Add(new DeviceItemViewModel(device));
            await PersistAsync();
            PairStatus = "";
            _ = RefreshStatesInternalAsync();
            return true;
        }
        catch (Exception ex)
        {
            PairStatus = ex.Message;
            return false;
        }
    }

    // ─── Test PJLink ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task TestPjLink()
    {
        var device = new ProjectionDevice
        {
            Type = "pjlink",
            Ip = NewIp.Trim(),
            Password = NewPassword,
            Port = int.TryParse(NewPort, out var p) ? p : 4352
        };
        try
        {
            var (_, message) = await _pjlink.TestAsync(device);
            TestResult = message;
        }
        catch (Exception ex)
        {
            TestResult = ex.Message;
        }
    }

    // ─── Test Sony Bravia ────────────────────────────────────────────────

    [RelayCommand]
    private async Task TestSony()
    {
        var device = new ProjectionDevice
        {
            Type = "sony",
            Ip = NewIp.Trim(),
            Password = NewPassword
        };
        try
        {
            var (_, message) = await _sony.TestAsync(device);
            TestResult = message;
        }
        catch (Exception ex)
        {
            TestResult = ex.Message;
        }
    }

    // ─── Ręczne dodanie (PJLink / Sony / WoL) ────────────────────────────

    [RelayCommand]
    private async Task AddDevice()
    {
        AddError = "";
        var name = NewName.Trim();
        var ip = NewIp.Trim();
        var mac = NewMac.Trim();

        if (AddType == "pjlink")
        {
            if (ip.Length == 0)
            {
                AddError = LocalizationManager.Get("Devices.ErrIp");
                return;
            }
            var device = new ProjectionDevice
            {
                Type = "pjlink",
                Name = name.Length > 0 ? name : ip,
                Label = TrimLabel(NewLabel),
                Ip = ip,
                Password = NewPassword,
                Port = int.TryParse(NewPort, out var p) && p > 0 ? p : 4352
            };
            Devices.Add(new DeviceItemViewModel(device));
        }
        else if (AddType == "sony")
        {
            if (ip.Length == 0)
            {
                AddError = LocalizationManager.Get("Devices.ErrIp");
                return;
            }
            if (NewPassword.Trim().Length == 0)
            {
                AddError = LocalizationManager.Get("Devices.ErrPsk");
                return;
            }
            var device = new ProjectionDevice
            {
                Type = "sony",
                Name = name.Length > 0 ? name : ip,
                Label = TrimLabel(NewLabel),
                Ip = ip,
                Password = NewPassword,
                Mac = mac
            };
            Devices.Add(new DeviceItemViewModel(device));
        }
        else // wol
        {
            if (mac.Length == 0)
            {
                AddError = LocalizationManager.Get("Devices.ErrMac");
                return;
            }
            var device = new ProjectionDevice
            {
                Type = "wol",
                Name = name.Length > 0 ? name : mac,
                Label = TrimLabel(NewLabel),
                Mac = mac
            };
            Devices.Add(new DeviceItemViewModel(device));
        }

        await PersistAsync();
        ClearForm();
        _ = RefreshStatesInternalAsync();
    }

    private void ClearForm()
    {
        NewName = "";
        NewLabel = "";
        NewIp = "";
        NewMac = "";
        NewPassword = "";
        NewPort = "4352";
        TestResult = "";
        AddError = "";
    }
}
