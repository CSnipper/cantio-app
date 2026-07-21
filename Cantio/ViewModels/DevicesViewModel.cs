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

    public DeviceItemViewModel(ProjectionDevice device)
    {
        Device = device;
        _editName = device.Name;
    }

    public string Name => Device.Name;
    public string Type => Device.Type;

    /// <summary>Czytelna etykieta typu do UI.</summary>
    public string TypeLabel => Device.Type switch
    {
        "samsung" => "Samsung TV",
        "pjlink" => "PJLink",
        "sony" => "Sony Bravia",
        _ => "Wake-on-LAN"
    };

    public void RefreshName()
    {
        OnPropertyChanged(nameof(Name));
        EditName = Device.Name;
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

    [ObservableProperty] private bool _hasDevices;
    [ObservableProperty] private bool _isPopupOpen;

    // ─── Formularz dodawania ─────────────────────────────────────────────
    [ObservableProperty] private string _addType = "samsung";
    [ObservableProperty] private string _newName = "";
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
    }

    private async Task PersistAsync()
    {
        await _control.SaveDevicesAsync(Devices.Select(i => i.Device).ToList());
        HasDevices = Devices.Count > 0;
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
        finally { item.IsBusy = false; }
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
        finally { item.IsBusy = false; }
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
        item.IsEditing = true;
    }

    [RelayCommand]
    private async Task CommitRename(DeviceItemViewModel? item)
    {
        if (item is null) return;
        var name = (item.EditName ?? "").Trim();
        if (name.Length > 0) item.Device.Name = name;
        item.IsEditing = false;
        item.RefreshName();
        await PersistAsync();
    }

    // ─── Popup na pasku górnym ───────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleDevicePopup()
    {
        IsPopupOpen = !IsPopupOpen;
        if (IsPopupOpen)
            await RefreshStatesInternalAsync();
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
        PairStatus = LocalizationManager.Get("Devices.PairConfirmOnTv");
        var device = new ProjectionDevice
        {
            Type = "samsung",
            Name = string.IsNullOrWhiteSpace(tv.Name) ? tv.Ip : tv.Name,
            Ip = tv.Ip,
            Mac = tv.Mac
        };
        try
        {
            var (ok, token, error) = await _samsung.PairAsync(device);
            if (!ok)
            {
                PairStatus = string.IsNullOrEmpty(error)
                    ? LocalizationManager.Get("Devices.PairFailed")
                    : error;
                return;
            }
            device.Token = token ?? "";
            Devices.Add(new DeviceItemViewModel(device));
            await PersistAsync();
            Discovered.Remove(tv);
            PairStatus = "";
            _ = RefreshStatesInternalAsync();
        }
        catch (Exception ex)
        {
            PairStatus = ex.Message;
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
        NewIp = "";
        NewMac = "";
        NewPassword = "";
        NewPort = "4352";
        TestResult = "";
        AddError = "";
    }
}
