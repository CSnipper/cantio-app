using Cantio.Helpers;
using Cantio.Models;
using Cantio.Services;
using Cantio.ViewModels;
using Cantio.Views;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Cantio;

public partial class MainWindow : Window
{
    private readonly DisplayViewModel _vm;
    private readonly ImportViewModel _importVm;
    private readonly SzablonViewModel _szablonVm;
    private readonly ShortcutService _shortcutService;
    private readonly ShortcutsViewModel _shortcutsVm;
    private readonly AboutViewModel _aboutVm = new();
    private RemoteControlViewModel _remoteControl = null!;
    private DeviceControlService _deviceControl = null!;
    private DevicesViewModel _devicesVm = null!;
    /// <summary>Trwające wysyłki obrazków z Pilota — jeden magazyn na aplikację (v1.69).</summary>
    private readonly PilotImages.UploadStore _pilotUploads = new();
    /// <summary>Zmiana ekranu projekcji „na próbę" z tabletu — jeden stan na aplikację.</summary>
    private readonly SystemSettingsTrial _systemSettingsTrial = new();
    /// <summary>
    /// Blokada „jedna operacja konserwacyjna naraz" (etap 4A). Stan jest APLIKACYJNY, nie
    /// per-klient: operacja trwa dalej, gdy tablet się rozłączy, a drugi tablet ma wtedy
    /// dostać <c>busy</c>.
    /// </summary>
    private readonly PilotMaintenance.Runner _maintenanceRunner = new();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Alt+F4 — nie przechwytuj, pozwól WPF zamknąć okno
        if (e.Key == Key.System && e.SystemKey == Key.F4
            && e.KeyboardDevice.Modifiers == ModifierKeys.Alt)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        var mods = e.KeyboardDevice.Modifiers;

        // SongSearch działa również gdy fokus jest na TextBox (jak Ctrl+F)
        if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.SongSearch))
        {
            ShowPane(PaneShow, TabShow);
            SearchBoxShow.Focus();
            SearchBoxShow.SelectAll();
            e.Handled = true;
            return;
        }

        // Configured tab / search shortcuts (skip when focus is on text input)
        if (e.OriginalSource is not TextBox && e.OriginalSource is not RichTextBox)
        {
            if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.TabShow))
            { ShowPane(PaneShow, TabShow); e.Handled = true; return; }
            if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.TabTemplate))
            { ShowPane(PaneTemplate, TabTemplate); e.Handled = true; return; }
            if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.TabImport))
            { ShowPane(PaneImport, TabImport); e.Handled = true; return; }
            if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.SearchOpen))
            { _vm.OpenSetlistSearchCommand.Execute(null); e.Handled = true; return; }
            // Włącz/wyłącz wszystkie TV i projektory — to samo co przycisk ⏻ na pasku górnym
            if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.PowerAll))
            {
                var cmd = _devicesVm?.TogglePowerAllCommand;
                if (cmd is not null && cmd.CanExecute(null)) cmd.Execute(null);
                e.Handled = true;
                return;
            }
        }

        // F1 → otwórz popup skrótów klawiaturowych
        if (e.Key == Key.F1 && e.OriginalSource is not TextBox)
        {
            OpenShortcutsPopup();
            e.Handled = true;
            return;
        }

        // Ctrl+S → zapisz zależnie od aktywnej zakładki (działa też gdy fokus jest na TextBox)
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HandleSave();
            e.Handled = true;
            return;
        }

        // Nie przechwytuj gdy fokus jest na polu tekstowym
        if (e.OriginalSource is TextBox || e.OriginalSource is RichTextBox)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        // Fokus na liście pieśni — nawigacja i Enter należą do listy, nie do zestawu
        if (IsFocusInside(SongListShow))
        {
            switch (e.Key)
            {
                // Natywna nawigacja ListBoksa
                case Key.Down:
                case Key.Up:
                case Key.Home:
                case Key.End:
                case Key.PageDown:
                case Key.PageUp:
                    base.OnPreviewKeyDown(e);
                    return;

                // Enter — to samo co przycisk „+” w wierszu: dodaj pieśń do zestawu
                case Key.Enter:
                    var song = SongListShow.SelectedItem as Song;
                    var addCmd = _vm.AddToSetlistCommand;
                    if (song is not null && addCmd.CanExecute(song))
                        addCmd.Execute(song);
                    e.Handled = true;
                    return;
            }
        }

        // Litera A–Z w trakcie projekcji — skok do następnej pozycji zestawu o tym tytule
        if (TryLetterJumpInSetlist(e.Key, mods))
        {
            e.Handled = true;
            return;
        }

        // Skróty projekcji działają zawsze — niezależnie od fokusu listy pieśni
        _vm.HandleKey(e.Key, e.KeyboardDevice.Modifiers);
        e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// Nawigacja literami po liście zestawu (tylko przy otwartej projekcji — poza nią litery
    /// nie mają w oknie znaczenia i nie ma po co odbierać ich innym kontrolkom).
    /// Sam skok ZAZNACZA pozycję, świadomie NIE ładując jej na ekran: operator szuka pieśni
    /// w trakcie trwającej projekcji i wyświetla ją dopiero podwójnym klikiem / 👁.
    /// Zwraca true, gdy klawisz został zużyty na nawigację.
    /// </summary>
    private bool TryLetterJumpInSetlist(Key key, ModifierKeys mods)
    {
        if (key is < Key.A or > Key.Z) return false;
        // Shift wolno (wielka litera to ta sama litera), Ctrl/Alt/Win nie — tam żyją skróty,
        // a prawy Alt (Ctrl+Alt) to polskie diakrytyki z klawiatury.
        if (mods is not ModifierKeys.None and not ModifierKeys.Shift) return false;
        if (!_vm.IsProjectionOpen) return false;

        // Litera przypisana ręcznie jako skrót użytkownika należy do skrótu, nie do nawigacji.
        foreach (var action in ShortcutService.AllActions)
            if (_shortcutService.IsMatch(key, mods, action)) return false;

        var items = _vm.SetlistItems;
        if (items.Count == 0) return false;

        char letter = (char)('A' + (key - Key.A));
        var current = _vm.SelectedSetlistItem;
        int currentIndex = current is null ? -1 : items.IndexOf(current);

        var hit = SetlistLetterJump.FindNext(SetlistLetterJump.TitlesOf(items), currentIndex, letter);
        if (hit is not null)
        {
            _vm.SelectedSetlistItem = items[hit.Value];
            SetlistListBox.ScrollIntoView(items[hit.Value]);
        }
        // Brak trafienia też zjadamy — inaczej litera poleciałaby do HandleKey na dole.
        return true;
    }

    // Drag & drop listy zestawu

    private int _dragFromIndex = -1;

    private void SetlistItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe)
        {
            if (fe.DataContext is SetlistItem item)
            {
                _dragFromIndex = _vm.SetlistItems.IndexOf(item);
                if (_dragFromIndex >= 0)
                    DragDrop.DoDragDrop(fe, item, DragDropEffects.Move);
            }
        }
    }

    private void SetlistItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SetlistItem target)
        {
            int toIndex = _vm.SetlistItems.IndexOf(target);
            if (_dragFromIndex >= 0 && toIndex >= 0 && _dragFromIndex != toIndex)
            {
                var item = _vm.SetlistItems[_dragFromIndex];
                _vm.SetlistItems.RemoveAt(_dragFromIndex);
                _vm.SetlistItems.Insert(toIndex, item);
                _dragFromIndex = -1;
            }
        }
    }

    private readonly DatabaseService _db;

    public MainWindow(DatabaseService db)
    {
        InitializeComponent();

        _db = db;
        _shortcutService = new ShortcutService();

        _vm = new DisplayViewModel(db, new ProjectionViewModel(), _shortcutService);
        // Tryb serwerowy: mini PC nie ma operatora, a projekcja jest Topmost — modal schowałby się
        // pod nią i zawiesił program. Odmawiamy (nic destrukcyjnego bez zgody) i logujemy.
        _vm.ConfirmRequested = msg =>
        {
            if (!AppModeRules.CanShowBlockingDialog(AppMode.Current) && !IsVisible)
            {
                AppLog.Write("UI", $"Tryb serwerowy — pominięto pytanie „{msg}”, operacja odrzucona.");
                return false;
            }
            return MessageBox.Show(this, msg, "Cantio", MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes;
        };
        // Pieśń otwarta w edytorze skasowana z tabletu — edytor już się zamknął, operator
        // musi się dowiedzieć, dlaczego zniknęła mu praca sprzed chwili.
        _vm.NotifyEditorClosedExternally = songId =>
        {
            AppLog.Write("Pilot", $"Pieśń {songId} skasowana zdalnie — zamknięto otwarty edytor.");
            if (AppModeRules.CanShowBlockingDialog(AppMode.Current) && IsVisible)
                MessageBox.Show(this,
                    "Edytowana pieśń została w międzyczasie usunięta z tabletu.\n" +
                    "Edytor zamknięto — niezapisane zmiany przepadły.",
                    "Cantio", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        DataContext = _vm;

        _importVm = new ImportViewModel(db);

        _szablonVm = new SzablonViewModel(db, _vm.Projection, _vm);
        _szablonVm.Saved += () =>
        {
            _vm.RebuildSlides();
            // „ZAPISZ USTAWIENIA" w zakładce WYGLĄD → ten sam komunikat do tabletów, co po
            // zmianie z Pilota. Jeden builder, dwie ścieżki — bez drugiej listy pól.
            // (Pole _remoteControl jest ustawiane niżej w tym samym konstruktorze; Saved
            // odpala się dopiero po interakcji użytkownika, więc nigdy nie jest null.)
            _ = _remoteControl.BroadcastJsonAsync(PilotDisplaySettings.BuildDataJson(db.GetSettings()));
        };
        _szablonVm.DioceseChanged += RefreshLitDay;
        // Zmiana wydania lekcjonarza → przeładuj bieżącą pieśń (filtr zwrotek w LoadVersesAsync).
        _szablonVm.LectionaryChanged += async () => await _vm.ReloadCurrentSongAsync();
        PaneTemplate.DataContext = _szablonVm;
        PaneImport.DataContext = _szablonVm;
        ImportColumn.DataContext = _importVm;
        ImportLogColumn.DataContext = _importVm;

        _shortcutsVm = new ShortcutsViewModel(db, _shortcutService);

        _importVm.SetlistsImported += async () => await _vm.LoadPinnedSetlistsAsync();

        PaneAbout.DataContext = _aboutVm;

        _remoteControl = new RemoteControlViewModel(db);
        PilotPanel.DataContext = _remoteControl;

        _deviceControl = new DeviceControlService(db);
        _devicesVm = new DevicesViewModel(_deviceControl);
        DevicesPanelView.DataContext = _devicesVm;
        DevicesBarHost.DataContext = _devicesVm;

        // Pilot: sterowanie urządzeniami (włącz/wyłącz wszystkie) + rozgłaszanie stanu
        _remoteControl.DevicesPowerAllRequested += async on =>
        {
            await _devicesVm.SetAllPowerFromRemoteAsync(on);
        };
        _devicesVm.DevicesChanged += async () =>
        {
            var (state, count) = _devicesVm.GetAggregateState();
            try { await _remoteControl.BroadcastDevicesAsync(state, count); } catch { }
        };

        _remoteControl.NextRequested  += (_, _) =>
            Dispatcher.Invoke(() => _vm.NextSlideCommand.Execute(null));
        _remoteControl.PrevRequested  += (_, _) =>
            Dispatcher.Invoke(() => _vm.PrevSlideCommand.Execute(null));
        _remoteControl.BlankRequested += (_, _) =>
            Dispatcher.Invoke(() => _vm.ToggleBlankCommand.Execute(null));
        _remoteControl.GotoRequested += idx =>
            Dispatcher.Invoke(() =>
            {
                if (idx >= 0 && idx < _vm.SlideList.Count)
                    _vm.CurrentSlideIndex = idx;
            });
        _remoteControl.GotoSongRequested += idx =>
            Dispatcher.Invoke(() =>
            {
                if (idx >= 0 && idx < _vm.SetlistItems.Count)
                    _vm.DisplaySetlistItemCommand.Execute(_vm.SetlistItems[idx]);
            });
        _remoteControl.SetlistRemoveRequested += idx =>
            Dispatcher.Invoke(() =>
            {
                if (idx >= 0 && idx < _vm.SetlistItems.Count)
                    _vm.RemoveFromSetlistCommand.Execute(_vm.SetlistItems[idx]);
            });
        _remoteControl.SetlistMoveRequested += (from, to) =>
            Dispatcher.Invoke(() =>
            {
                if (from >= 0 && to >= 0 &&
                    from < _vm.SetlistItems.Count && to < _vm.SetlistItems.Count &&
                    from != to)
                    _vm.SetlistItems.Move(from, to);
            });
        _remoteControl.SetlistAddRequested += songId =>
            _ = Dispatcher.InvokeAsync(async () =>
            {
                var song = await db.GetSongWithVersesAsync(songId);
                if (song != null) _vm.AddToSetlistCommand.Execute(song);
            });
        // Gest „w lewo" na liście PIEŚNI w Pilocie tabletowym = odpowiednik oka 👁 w oknie Cantio:
        // pieśń ląduje NA EKRANIE, ale NIE w zestawie (i nie rusza podświetlenia pozycji zestawu).
        _remoteControl.ShowSongRequested += songId =>
            _ = Dispatcher.InvokeAsync(async () =>
            {
                var song = await db.GetSongWithVersesAsync(songId);
                if (song != null) _vm.DisplaySongCommand.Execute(song);
            });
        _remoteControl.SetlistClearRequested += () =>
            _ = Dispatcher.InvokeAsync(() => _vm.ClearSetlistCommand.Execute(null));

        _remoteControl.SetlistRestoreRequested += (items, activeIndex) =>
            _ = Dispatcher.InvokeAsync(async () =>
            {
                _vm.ClearSetlistCommand.Execute(null);
                foreach (var entry in items)
                {
                    // Tekst jednorazowy wraca z pełną treścią (tą samą ścieżką co przycisk 📝),
                    // pieśń po ID.
                    if (entry.IsText)
                    {
                        _vm.ApplyTextItem(null, entry.CustomTitle, entry.CustomText);
                        continue;
                    }
                    // Obrazek (v1.69) wraca, o ile desktop MA jego plik — telefon przysyła sam ref.
                    // Brak pliku = pozycja pomijana bez błędu (zestaw mógł powstać na innym PC).
                    if (entry.IsImage)
                    {
                        if (PilotImages.RefExists(entry.ImageRef))
                            _vm.ApplyImageItem(entry.ImageRef!, insertAfterSelected: false);
                        else
                            AppLog.Write("Pilot",
                                $"Wczytanie zestawu: pozycja-obrazek pominięta, brak pliku „{entry.ImageRef}”");
                        continue;
                    }
                    var song = await db.GetSongWithVersesAsync(entry.Id);
                    if (song != null) _vm.AddToSetlistCommand.Execute(song);
                }
                // AddToSetlist zostawia aktywną OSTATNIĄ dodaną pieśń (funkcja z v1.49);
                // aktywna ma być ta podświetlona na telefonie, a bez activeIndex — pierwsza.
                var idx = SetlistRestore.ResolveActiveIndex(activeIndex, _vm.SetlistItems.Count);
                if (idx >= 0) _vm.DisplaySetlistItemCommand.Execute(_vm.SetlistItems[idx]);
            });

        _remoteControl.GetSetlistsRequested += async ws =>
        {
            try
            {
                await _remoteControl.SendToClientAsync(ws, await PilotSetlistSync.BuildSetlistsJsonAsync(db));
            }
            catch { }
        };

        _remoteControl.GetSetlistDetailRequested += async (ws, setlistId) =>
        {
            try
            {
                var detail = await db.GetSetlistDetailAsync(setlistId);
                if (detail == null) return;
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type      = "setlist_detail",
                    id        = detail.Value.Id,
                    name      = detail.Value.Name,
                    group     = detail.Value.Group ?? "",
                    updatedAt = detail.Value.UpdatedAt,
                    songs     = PilotSetlistItems.ToJsonArray(detail.Value.Songs)
                });
                await _remoteControl.SendToClientAsync(ws, json);
            }
            catch { }
        };

        _remoteControl.SetlistSyncPushRequested += async (ws, rawJson) =>
        {
            try
            {
                // Konflikt (zmiana po obu stronach) → PilotSetlistSync zwraca setlist_sync_conflict zamiast acka
                var json = await PilotSetlistSync.HandlePushAsync(db, rawJson);
                if (json != null) await _remoteControl.SendToClientAsync(ws, json);
            }
            catch { }
        };

        _remoteControl.SetlistDeleteRequested += async (ws, setlistId) =>
        {
            try
            {
                var (json, existed) = await PilotSetlistSync.HandleDeleteAsync(db, setlistId);
                if (existed)
                    _ = Dispatcher.InvokeAsync(async () =>
                        await _vm.OnSetlistDeletedExternallyAsync(setlistId));
                await _remoteControl.SendToClientAsync(ws, json);
            }
            catch { }
        };

        _remoteControl.OpenSetlistRequested += (ws, setlistId) =>
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var setlist = await db.GetSetlistWithItemsAsync(setlistId);
                    if (setlist == null) return;
                    await _vm.LoadPinnedSetlistCommand.ExecuteAsync(setlist);
                }
                catch { }
            });

        _remoteControl.SyncPushRequested += async (ws, rawJson) =>
        {
            try
            {
                var mapping = await db.SyncPushSongsAsync(rawJson);
                var ackJson = JsonSerializer.Serialize(new
                {
                    type    = "sync_push_ack",
                    mapping = mapping.Select(m => new { localId = m.localId, assignedId = m.assignedId })
                });
                await _remoteControl.SendToClientAsync(ws, ackJson);
            }
            catch { }
        };
        _remoteControl.GetSongsRequested += async (ws, offset, limit) =>
        {
            // Priorytet: Pilot MUSI dostać odpowiedź na KAŻDE get_songs. Cisza jest najgorsza —
            // stary połknięty catch{} przy dowolnym wyjątku nie wysyłał nic, a Pilot wisiał na
            // stronie 0 → pusta biblioteka (regresja produkcyjna). Serializacja jest już odporna
            // per pieśń (PilotSongSync); ten catch broni przed wyjątkiem z ODCZYTU z DB — wtedy
            // leci pusta strona z żądanym offsetem, żeby Pilot nie wisiał.
            int total = 0;
            try
            {
                (total, var items) = await db.GetSongsForSyncAsync(offset, limit);
                // Kształt komunikatu (w tym CategoryId == NULL → 0) składa PilotSongSync
                await _remoteControl.SendToClientAsync(ws,
                    PilotSongSync.BuildSongsDataJson(offset, total, items));
            }
            catch (Exception ex)
            {
                AppLog.Write("Pilot", $"get_songs (offset={offset}, limit={limit}) nieudany — " +
                                      $"wysyłam pustą stronę: {ex.GetType().Name}: {ex.Message}");
                try
                {
                    await _remoteControl.SendToClientAsync(ws,
                        PilotSongSync.BuildSongsDataJson(offset, total, System.Array.Empty<Song>()));
                }
                catch (Exception sendEx)
                {
                    AppLog.Write("Pilot", $"get_songs: nie udało się wysłać nawet pustej strony: {sendEx.Message}");
                }
            }
        };
        // ─── Ratunek dla mini PC bez klawiatury (status / restart / projekcja) ───
        // Ack na restart/open/close wysyła sam RemoteControlServer, ZANIM tu dotrze —
        // inaczej przy restarcie Pilot nigdy by się nie dowiedział, że komenda doszła.
        _remoteControl.StatusRequested += async ws =>
        {
            try
            {
                var info = await Dispatcher.InvokeAsync(() => new PilotStatusInfo(
                    Version:          PilotStatus.AppVersion(),
                    Mode:             AppMode.ToSettingValue(AppMode.Current),
                    ProjectionOpen:   _vm.IsProjectionOpen,
                    ProjectionScreen: _vm.ProjectionScreenIndex,
                    ScreenCount:      DisplayViewModel.ScreenCount,
                    PairedDevices:    _remoteControl.PairedDeviceCount,
                    UptimeSeconds:    PilotStatus.UptimeSeconds()));
                await _remoteControl.SendToClientAsync(ws, PilotStatus.BuildStatusJson(info));
            }
            catch { }
        };

        _remoteControl.RestartAppRequested += ws =>
            Dispatcher.InvokeAsync(() => RestartApplication("na żądanie Pilota"));

        _remoteControl.ProjectionRequested += (ws, open) =>
            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (open) await _vm.OpenProjectionFromRemoteAsync();
                    else _vm.CloseProjectionFromRemote();
                }
                catch (Exception ex) { AppLog.Write("Pilot", $"Projekcja z Pilota: {ex.Message}"); }
            });

        // ─── Kategorie i grupy zestawów z Pilota ───
        // Cała logika (parse → operacja → odpowiedź + broadcast) siedzi w PilotCategorySync;
        // tu zostaje wyłącznie wysyłka i odświeżenie UI TĄ SAMĄ ścieżką co po edycji lokalnej.
        _remoteControl.CategoryCommandRequested += async (ws, raw) =>
        {
            try
            {
                var result = await PilotCategorySync.HandleAsync(db, raw);
                if (result.Response  != null) await _remoteControl.SendToClientAsync(ws, result.Response);
                if (result.Broadcast != null) await _remoteControl.BroadcastJsonAsync(result.Broadcast);
                if (result.Scope != PilotCategorySync.RefreshScope.None)
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            if (result.Scope == PilotCategorySync.RefreshScope.Categories)
                                await _vm.RefreshCategoriesExternallyAsync();
                            else
                                await _vm.RefreshSetlistGroupsExternallyAsync();
                        }
                        catch (Exception ex) { AppLog.Write("Pilot", $"Odświeżenie list: {ex.Message}"); }
                    });
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda kategorii/grup: {ex.Message}"); }
        };

        // ─── Przypinanie zestawów (panel PRZYPIĘTE) ───
        // Stan jest wspólny: komenda z Pilota zmienia bazę i wraca do WSZYSTKICH klientów jako
        // `setlist_pinned`, a okno Cantio odświeża panel tą samą metodą co po kliknięciu pinezki.
        _remoteControl.SetlistPinCommandRequested += async (ws, raw) =>
        {
            try
            {
                var result = await PilotSetlistPin.HandleAsync(db, raw);
                if (result.Response  != null) await _remoteControl.SendToClientAsync(ws, result.Response);
                if (result.Broadcast != null) await _remoteControl.BroadcastJsonAsync(result.Broadcast);
                if (result.Changed)
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        try { await _vm.ApplyExternalPinAsync(result.DesktopId, result.Pinned); }
                        catch (Exception ex) { AppLog.Write("Pilot", $"Odświeżenie PRZYPIĘTYCH: {ex.Message}"); }
                    });
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda przypięcia: {ex.Message}"); }
        };

        // Pinezka kliknięta w oknie Cantio → ten sam komunikat do Pilotów (jeden builder, dwie ścieżki).
        _vm.SetlistPinChanged += (setlistId, pinned) =>
            _ = _remoteControl.BroadcastJsonAsync(PilotSetlistPin.BuildPinnedJson(setlistId, pinned));

        // ─── „Przypnij tydzień" z Pilota ───
        // Ta sama metoda co przycisk w oknie (PilotPinWeek.RunAsync pod spodem), więc UI desktopu
        // odświeża się po drodze, a piny lecą istniejącymi broadcastami `setlist_pinned`.
        // Dialogu podsumowania NIE pokazujemy — komenda dostaje go ackiem.
        _remoteControl.PinNextWeekRequested += async ws =>
        {
            try
            {
                var result = await Dispatcher.InvokeAsync(() => _vm.PinNextWeekAsync(announce: false))
                                             .Task.Unwrap();
                await _remoteControl.SendToClientAsync(ws, PilotPinWeek.BuildAckJson(result));
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda przypnij tydzień: {ex.Message}"); }
        };

        // Podpisy obchodów pod przypiętymi zestawami — JEDEN komunikat po każdym przeładowaniu
        // listy PRZYPIĘTE (pin, unpin, przypnij tydzień, zmiana diecezji, import).
        _vm.PinnedListRefreshed += () =>
            _ = _remoteControl.BroadcastJsonAsync(
                PilotPinWeek.BuildCelebrationsJson(BuildPinnedCaptions()));

        // Kategorie i grupy zestawów zmienione W OKNIE — ta sama obietnica protokołu, co przy
        // komendach z tabletu: broadcast po KAŻDEJ mutacji, niezależnie od tego, kto ją zrobił.
        // Komunikaty składa wyłącznie PilotCategorySync (jeden builder, dwie ścieżki).
        _vm.CategoriesChangedLocally += async () =>
        {
            try
            {
                await _remoteControl.BroadcastJsonAsync(
                    PilotCategorySync.BuildCategoriesJson(await db.GetCategoriesAsync()));
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Broadcast kategorii: {ex.Message}"); }
        };
        _vm.SetlistGroupsChangedLocally += async () =>
        {
            try
            {
                await _remoteControl.BroadcastJsonAsync(
                    PilotCategorySync.BuildGroupsJson(await db.GetSetlistGroupsAsync()));
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Broadcast grup zestawów: {ex.Message}"); }
        };

        // Zmiana diecezji zmienia obchody, więc i podpisy na liście PRZYPIĘTE.
        _szablonVm.DioceseChanged += async () =>
        {
            try { await _vm.LoadPinnedSetlistsAsync(); }
            catch (Exception ex) { AppLog.Write("Pilot", $"Odświeżenie PRZYPIĘTYCH: {ex.Message}"); }
        };

        // Podsumowanie po kliknięciu „Przypnij tydzień" w oknie. W trybie serwerowym cisza —
        // przy mini PC bez klawiatury nikt tego okna nie zamknie (zasada: zero blokujących dialogów).
        _vm.WeekPinned += result =>
        {
            if (AppMode.IsServer) return;
            MessageBox.Show(this, BuildPinWeekSummary(result),
                TryFindResource("PinWeek.Summary.Title") as string ?? "Cantio",
                MessageBoxButton.OK, MessageBoxImage.Information);
        };

        // ─── Ustawienia projekcji (wygląd) z Pilota ───
        // Logika siedzi w PilotDisplaySettings; tu zostaje wysyłka, broadcast i odświeżenie
        // desktopu DOKŁADNIE tą samą ścieżką co „ZAPISZ USTAWIENIA" w zakładce WYGLĄD
        // (ApplySettings + RebuildSlides) — bez tego zmiana nie weszłaby na ekran.
        _remoteControl.DisplaySettingsCommandRequested += async (ws, raw) =>
        {
            try
            {
                var result = await PilotDisplaySettings.HandleAsync(db, raw);
                if (result.Response  != null) await _remoteControl.SendToClientAsync(ws, result.Response);
                if (result.Broadcast != null) await _remoteControl.BroadcastJsonAsync(result.Broadcast);
                if (result.Changed)
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await _szablonVm.ApplyExternalSettingsAsync();
                            _vm.RebuildSlides();
                        }
                        catch (Exception ex) { AppLog.Write("Pilot", $"Odświeżenie wyglądu: {ex.Message}"); }
                    });
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda ustawień wyglądu: {ex.Message}"); }
        };

        // ─── Ustawienia systemowe (tryb / ekran projekcji / język) z Pilota ───
        // Jedyne wyjście z trybu serwerowego na mini PC bez klawiatury. Logika (walidacja,
        // wymuszenie autostartu serwera pilota, bezpiecznik ekranu) siedzi w PilotSystemSettings;
        // tu zostaje wysyłka, broadcast, przestawienie okna projekcji i odświeżenie zakładki
        // USTAWIENIA — tą samą ścieżką co przy ustawieniach wyglądu (ApplyExternalSettingsAsync
        // celowo NIE odpala `Saved`, więc drugi broadcast nie poleci).
        // Autostart Windows stoi w rejestrze, nie w tabeli `settings` — rdzeń kompiluje się też
        // pod Androida i rejestru nie zna, więc dostaje port od gospodarza. Zapis idzie DOKŁADNIE
        // tą samą ścieżką co checkbox w oknie (OnRunOnStartupChanged), żeby zakładka USTAWIENIA
        // nie kłamała o stanie rejestru; odczyt czyta rejestr, a nie zapamiętane życzenie.
        PilotSystemSettings.RunOnStartup = new PilotSystemSettings.RunOnStartupPort(
            Read:  () => Dispatcher.Invoke(() => _szablonVm.RunOnStartup),
            Write: v  => Dispatcher.Invoke(() => _szablonVm.RunOnStartup = v));

        // Serwer pilota (PIN, tokeny, port) — rdzeń mówi CO, wykonuje RemoteControlViewModel
        // swoimi istniejącymi ścieżkami (te same, co przyciski w zakładce USTAWIENIA), więc
        // kod QR i ekran parowania na projekcji odświeżają się same.
        PilotSystemSettings.PilotServer = new PilotSystemSettings.PilotServerPort(
            IsRunning:        () => Dispatcher.Invoke(() => _remoteControl.IsRunning),
            PairedDevices:    () => Dispatcher.Invoke(() => _remoteControl.PairedDeviceCount),
            SetPin:           p  => Dispatcher.Invoke(() => _remoteControl.SetPinFromRemote(p)),
            EnableRequirePin: () => Dispatcher.Invoke(() => _remoteControl.EnableRequirePinFromRemote()),
            ApplyPort:        p  => Dispatcher.Invoke(() => _remoteControl.ApplyPortFromRemote(p)),
            ForgetDevices:    p  => Dispatcher.Invoke(() => _remoteControl.ForgetPairedDevicesFromRemote(p)));

        _remoteControl.SystemSettingsCommandRequested += async (ws, raw) =>
        {
            try
            {
                var result = await PilotSystemSettings.HandleAsync(
                    db, raw, BuildScreenList(), _systemSettingsTrial);
                await ApplySystemSettingsResultAsync(result, ws);

                // Odliczanie bezpiecznika. Budzik jest „głupi": po przebudzeniu pyta stan
                // maszyny, więc przedłużenie odliczania drugą zmianą (albo potwierdzenie
                // z tabletu) zwyczajnie zamienia go w nic-nie-robienie. Stan próby żyje
                // w `_systemSettingsTrial` — POZA gniazdem klienta, bo potwierdzenie zmiany
                // portu przychodzi INNYM połączeniem (rozłączenie klienta go nie rusza).
                if (result.ApplyScreen != null && _systemSettingsTrial.IsScreenPending(DateTime.UtcNow))
                    ScheduleSystemSettingsExpiry(db, SystemSettingsTrial.DefaultSeconds + 1);
                if (result.ApplyPort != null && _systemSettingsTrial.IsPortPending(DateTime.UtcNow))
                    ScheduleSystemSettingsExpiry(db, SystemSettingsTrial.PortSeconds + 1);
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda ustawień systemowych: {ex.Message}"); }
        };

        // ─── Folder wymiany i operacje konserwacyjne z Pilota (etap 4A) ───
        // Handler jest GŁUPI: cała logika (folder wymiany, blokada „jedna operacja naraz",
        // gwarancja komunikatu terminalnego) siedzi w PilotMaintenance. Tu zostaje kolejność,
        // która jest częścią kontraktu: ack WYCHODZI PIERWSZY, a długa operacja rusza dopiero
        // po nim i leci W TLE (bez `await`) — tablet ma dostać identyfikator zadania od razu,
        // a nie po skończonym pakowaniu archiwum.
        // Restart po operacji NIEODWRACALNEJ (etap 4B) — rdzeń mówi „teraz", wykonuje gospodarz
        // TĄ SAMĄ ścieżką co `restart_app`. Rdzeń kompiluje się też pod Androida i nie zna ani
        // `Application.Shutdown`, ani gniazda serwera pilota. Kolejność jest częścią kontraktu:
        // PilotMaintenance woła to DOPIERO po wysłaniu komunikatu terminalnego.
        PilotMaintenance.Restart = () =>
            Dispatcher.InvokeAsync(() => RestartApplication("po operacji konserwacyjnej"));

        _remoteControl.MaintenanceCommandRequested += async (ws, raw) =>
        {
            try
            {
                var result = PilotMaintenance.Handle(_maintenanceRunner, db, raw);
                if (result.Response != null) await _remoteControl.SendToClientAsync(ws, result.Response);
                if (result.Work != null)
                    _ = result.Work(json => _remoteControl.BroadcastJsonAsync(json));
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda konserwacji: {ex.Message}"); }
        };

        // ─── Edytor pieśni z Pilota ───
        // Logika siedzi w PilotSongEdit; tu zostaje wysyłka, broadcast `song_changed` i odświeżenie
        // okna Cantio TĄ SAMĄ ścieżką co zapis w edytorze pieśni (listy + przeładowanie pieśni,
        // która jest na ekranie, z kotwicą pozycji slajdu).
        _remoteControl.SongEditCommandRequested += async (ws, raw) =>
        {
            try
            {
                var result = await PilotSongEdit.HandleAsync(db, raw);
                if (result.Response  != null) await _remoteControl.SendToClientAsync(ws, result.Response);
                if (result.Broadcast != null) await _remoteControl.BroadcastJsonAsync(result.Broadcast);
                if (result.Change != PilotSongEdit.SongChange.None)
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await _vm.OnSongEditedExternallyAsync(result.SongId,
                                deleted: result.Change == PilotSongEdit.SongChange.Deleted);
                        }
                        catch (Exception ex) { AppLog.Write("Pilot", $"Odświeżenie pieśni: {ex.Message}"); }
                    });
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda edytora pieśni: {ex.Message}"); }
        };

        // ─── Tekst jednorazowy z Pilota ───
        // Mutacja dotyczy KOLEKCJI W PAMIĘCI (bieżący zestaw), nie bazy — dlatego logika siedzi
        // w czystym PilotTextItem, a wykonanie idzie przez DisplayViewModel.ApplyTextItem, czyli
        // tę samą ścieżkę co przycisk 📝 w oknie. Dodanie rozgłasza się samo (CollectionChanged),
        // EDYCJA W MIEJSCU nie — stąd jawny broadcast.
        _remoteControl.TextItemCommandRequested += async (ws, raw) =>
        {
            try
            {
                var request = PilotTextItem.Parse(raw);
                if (request.Operation == PilotTextItem.Kind.None) return;

                if (request.Denied)
                {
                    await _remoteControl.SendToClientAsync(ws, PilotTextItem.BuildDenial(request, request.Reason!));
                    return;
                }

                var reason = await Dispatcher.InvokeAsync(() =>
                {
                    if (request.IsAdd)
                    {
                        _vm.ApplyTextItem(null, request.Title, request.Text);
                        return null;
                    }

                    var item = request.Index >= 0 && request.Index < _vm.SetlistItems.Count
                        ? _vm.SetlistItems[request.Index] : null;
                    var denial = PilotTextItem.ValidateTarget(
                        request.Index, _vm.SetlistItems.Count, item?.IsTextItem ?? false);
                    if (denial != null) return denial;

                    _vm.ApplyTextItem(item, request.Title, request.Text);
                    return null;
                }).Task;

                await _remoteControl.SendToClientAsync(ws,
                    reason == null
                        ? PilotTextItem.BuildAck(request.Operation, true)
                        : PilotTextItem.BuildDenial(request, reason));

                // Edycja w miejscu nie rusza kolekcji, więc nikt inny tego nie rozgłosi.
                if (reason == null && request.IsUpdate) await BroadcastSetlistState();
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda tekstu jednorazowego: {ex.Message}"); }
        };

        // ─── Obrazki z Pilota ───
        // Logika i stan uploadów siedzą w PilotImages; tu zostaje wpięcie skalowania (koder żyje
        // tylko w WPF) i mutacja BIEŻĄCEGO zestawu przez DisplayViewModel.ApplyImageItem — tę samą
        // ścieżkę, co przycisk 🖼 w oknie. Broadcast `setlist` poleci sam z CollectionChanged.
        _remoteControl.ImageCommandRequested += async (ws, raw) =>
        {
            try
            {
                var result = PilotImages.Handle(raw, ws, _pilotUploads, PilotImageScaler.Scale);
                if (result.AddedImageRef != null)
                    await Dispatcher.InvokeAsync(() =>
                        _vm.ApplyImageItem(result.AddedImageRef, insertAfterSelected: true)).Task;
                if (result.Response != null) await _remoteControl.SendToClientAsync(ws, result.Response);
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Komenda obrazka: {ex.Message}"); }
        };
        _remoteControl.ClientDisconnected += ws => _pilotUploads.DropOwner(ws);

        // Ekran parowania na projektorze — gaśnie po pierwszym sparowanym urządzeniu,
        // wraca po „nowym PIN-ie" (który kasuje tokeny). Bez restartu aplikacji.
        _remoteControl.PairingStateChanged += () =>
            Dispatcher.InvokeAsync(RefreshPairingOverlay);

        _remoteControl.ClientConnected += async ws =>
        {
            try
            {
                var cats = await db.GetCategoriesAsync();
                await _remoteControl.SendToClientAsync(ws, PilotCategorySync.BuildCategoriesJson(cats));
                await BroadcastCurrentStateToAsync(ws);
                await BroadcastSetlistStateToAsync(ws);
                await _remoteControl.SendToClientAsync(ws,
                    PilotPinWeek.BuildCelebrationsJson(BuildPinnedCaptions()));
                var (devState, devCount) = _devicesVm.GetAggregateState();
                var devJson = JsonSerializer.Serialize(new { type = "devices", state = devState, count = devCount });
                await _remoteControl.SendToClientAsync(ws, devJson);
            }
            catch { }
        };

        async Task BroadcastCurrentState()
        {
            var text    = SlideLayoutService.StripFormatTags(_vm.CurrentSlideText);
            var title   = _vm.SelectedSong?.Title ?? "";
            var index   = _vm.CurrentSlideIndex;
            var total   = _vm.SlideList.Count;
            var isBlank = _vm.ScreenBlanked;
            var slides  = _vm.SlideList.Select(s => SlideLayoutService.StripFormatTags(s.Text).Trim()).ToList();
            var kinds   = _vm.SlideList.Select(SlideKind.FromSlide).ToList();
            var kind    = index >= 0 && index < _vm.SlideList.Count
                ? SlideKind.FromSlide(_vm.SlideList[index]) : SlideKind.Verse;
            // Pole leci TYLKO gdy bieżący slajd jest obrazkiem — inaczej Pilot pokazuje tekst jak dotąd.
            var imgRef  = _vm.CurrentImageRef;
            try { await _remoteControl.BroadcastAsync(text, title, index, total, isBlank, slides, kinds, kind, imgRef); }
            catch { }
        }

        async Task BroadcastCurrentStateToAsync(WebSocket ws)
        {
            var text    = SlideLayoutService.StripFormatTags(_vm.CurrentSlideText);
            var title   = _vm.SelectedSong?.Title ?? "";
            var index   = _vm.CurrentSlideIndex;
            var total   = _vm.SlideList.Count;
            var isBlank = _vm.ScreenBlanked;
            var slides  = _vm.SlideList.Select(s => SlideLayoutService.StripFormatTags(s.Text).Trim()).ToList();
            var kinds   = _vm.SlideList.Select(SlideKind.FromSlide).ToList();
            var kind    = index >= 0 && index < _vm.SlideList.Count
                ? SlideKind.FromSlide(_vm.SlideList[index]) : SlideKind.Verse;
            var json = RemoteControlServer.BuildSlideJson(
                text, title, index, total, isBlank, slides, kinds, kind, _vm.CurrentImageRef);
            await _remoteControl.SendToClientAsync(ws, json);
        }

        // Pozycje zestawu składa WYŁĄCZNIE PilotSetlistItems — obie ścieżki (broadcast i wysyłka
        // do świeżego klienta) biorą tę samą listę pól. Do v1.67 były to dwa niezależne obiekty
        // anonimowe i tekst jednorazowy leciał na łącze jako {id:0,title:""}.
        //
        // Snapshot niesie też TOŻSAMOŚĆ bieżącej listy (`LoadedSetlistId/Name`) — telefon musi
        // wiedzieć, który rekord bazy ma zaproponować do nadpisania przy „Zapisz". Gdy lista nie
        // pochodzi z zapisanego zestawu, id jest 0 i builder pomija oba pola.
        (List<PilotSetlistItems.Entry> Items, int ActiveIndex, int SetlistId, string Name) SetlistSnapshotForPilot()
            => (PilotSetlistItems.From(_vm.SetlistItems),
                _vm.SelectedSetlistItem != null ? _vm.SetlistItems.IndexOf(_vm.SelectedSetlistItem) : -1,
                _vm.LoadedSetlistId,
                _vm.LoadedSetlistName);

        async Task BroadcastSetlistState()
        {
            var (items, activeIndex, setlistId, name) = SetlistSnapshotForPilot();
            try { await _remoteControl.BroadcastSetlistAsync(items, activeIndex, setlistId, name); }
            catch { }
        }

        async Task BroadcastSetlistStateToAsync(WebSocket ws)
        {
            var (items, activeIndex, setlistId, name) = SetlistSnapshotForPilot();
            await _remoteControl.SendToClientAsync(ws,
                PilotSetlistItems.BuildSetlistJson(items, activeIndex, setlistId, name));
        }

        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName is nameof(DisplayViewModel.CurrentSlideIndex)
                               or nameof(DisplayViewModel.ScreenBlanked)
                               or nameof(DisplayViewModel.SlideList))
                await BroadcastCurrentState();
            // Tożsamość zestawu potrafi się zmienić BEZ zmiany pozycji („zapisz jako", nadpisanie
            // pod nową nazwą, odczepienie skasowanego rekordu) — wtedy CollectionChanged nie leci
            // i Pilot zostałby ze starym `setlistId`. Przy zwykłym wczytaniu zestawu poleci tu
            // broadcast nadmiarowy (obok tego z CollectionChanged) — to ten sam komunikat,
            // idempotentny po stronie telefonu, i nie tworzy pętli (broadcast nie rusza VM).
            if (e.PropertyName is nameof(DisplayViewModel.LoadedSetlistId)
                               or nameof(DisplayViewModel.LoadedSetlistName))
                await BroadcastSetlistState();
            if (e.PropertyName is nameof(DisplayViewModel.SelectedSetlistItem))
            {
                await BroadcastSetlistState();
                if (_vm.SelectedSetlistItem != null)
                    await Dispatcher.InvokeAsync(() => SetlistListBox.ScrollIntoView(_vm.SelectedSetlistItem));
            }
        };

        _vm.SetlistItems.CollectionChanged += async (_, e) =>
        {
            await BroadcastSetlistState();
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
                && e.NewItems?.Count > 0)
                await Dispatcher.InvokeAsync(() => SetlistListBox.ScrollIntoView(e.NewItems[e.NewItems.Count - 1]));
        };

        Loaded += async (_, _) =>
        {
            await _vm.InitializeAsync();
            await _vm.LoadOperatorNoteAsync();
            DiocesanCalendarService.CurrentDiocese = await db.GetSettingAsync("diocese") ?? "";
            RefreshLitDay();
            RestoreWindowPosition();
            await _remoteControl.InitAsync();
            RefreshPairingOverlay();
            await _devicesVm.InitAsync();
            await _aboutVm.CheckAndPromptAsync();

            var updateTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromHours(1) };
            updateTimer.Tick += async (_, _) => await _aboutVm.CheckAndPromptAsync();
            updateTimer.Start();
        };
        Closing += (_, _) => { _vm.StopLoop(); SaveWindowPosition(); _remoteControl.Dispose(); };
        KeyDown += _vm.OnKeyDown;

        InitClock();
    }

    /// <summary>
    /// Przenosi stan parowania na warstwę w oknie projekcji (tryb serwerowy: jedyne wyjście HDMI
    /// musi pokazać QR/PIN, bo nikt nie widzi okna Cantio). Decyzję podejmuje czysta reguła
    /// <see cref="AppModeRules.ShouldShowPairingScreen(AppModeKind,int)"/> — tu jest tylko przepisanie.
    /// </summary>
    private void RefreshPairingOverlay()
    {
        var p = _vm.Projection;

        // Awaria startu serwera wyprzedza ekran parowania: bez działającego serwera nie ma czego
        // parować, a PIN na ekranie byłby kłamstwem.
        p.ServerFailureReason = _remoteControl.StartFailure;
        p.ShowServerFailure = AppModeRules.ShouldShowServerFailure(
            AppMode.Current, _remoteControl.IsRunning, _remoteControl.StartFailure);

        var show = _remoteControl.ShouldShowPairingScreen;
        p.ShowPairing = show;
        if (!show) return;
        p.PairingPin = _remoteControl.Pin;
        p.PairingAddresses = new System.Collections.ObjectModel.ObservableCollection<string>(
            _remoteControl.AllLocalUrls);
        p.PairingQr = _remoteControl.PairingQrLarge;
    }

    // ─── Notatka dla zmienników ───────────────────────────────────────────────

    private void NotePopup_Opened(object sender, EventArgs e)
    {
        _vm.MarkNoteOpened();
        NoteTextBox.Focus();
    }

    private void NotePopup_Closed(object sender, EventArgs e)
    {
        if (_vm.SaveOperatorNoteCommand.CanExecute(null))
            _vm.SaveOperatorNoteCommand.Execute(null);
    }

    // ─── Zegar + dzień liturgiczny + formularze na pasku górnym ───────────────

    private DateOnly _clockDate;
    private Cantio.Models.LiturgicalDay? _clockDay;
    private List<Celebration> _clockCelebrations = [];

    private sealed record LitFormOption(string Label, string Name);

    private void InitClock()
    {
        UpdateClock();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateClock();
        timer.Start();
    }

    private void UpdateClock()
    {
        TxtClock.Text = DateTime.Now.ToString("HH:mm");
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _clockDate)
        {
            _clockDate = today;
            RefreshLitDay();
        }
        // co tick, żeby zmiana języka odświeżała nazwę okresu bez restartu
        var day = _clockDay;
        if (day == null) return;
        var season = TryFindResource("Season." + day.Group) as string ?? day.Group;
        TxtLitSeason.Text = day.Cycle.Length > 0
            ? $"{season} · {TryFindResource("Clock.Year") as string ?? "rok"} {day.Cycle}"
            : season;
    }

    /// <summary>Przelicza dzień liturgiczny, obchody i strzałkę formularzy (data/diecezja/wybór).</summary>
    public void RefreshLitDay()
    {
        var day = LiturgicalCalendarService.GetDay(_clockDate);
        _clockDay = day;
        _clockCelebrations = DiocesanCalendarService.ForDate(_clockDate);
        TxtLitDay.Text = DiocesanCalendarService.EffectiveSetlistName(_clockDate, day);
        BtnLitFormularies.Visibility = _clockCelebrations.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        LitSeasonDot.Fill = new System.Windows.Media.SolidColorBrush(day.Group switch
        {
            "adwent" or "wielki_post" => System.Windows.Media.Color.FromRgb(0x8e, 0x44, 0xad),
            "boznarodzenie" or "wielkanoc" => System.Windows.Media.Color.FromRgb(0xe8, 0xc9, 0x7a),
            _ => System.Windows.Media.Color.FromRgb(0x3d, 0x8b, 0x40),
        });
    }

    /// <summary>
    /// Podpisy obchodów dla Pilotów — czytane z listy PRZYPIĘTE, którą ViewModel właśnie policzył
    /// (bez drugiego zapytania do bazy i bez drugiej reguły; jedno źródło = <c>PinnedCelebrations</c>).
    /// </summary>
    private IEnumerable<KeyValuePair<int, string>> BuildPinnedCaptions() =>
        _vm.PinnedSetlists.Where(s => s.HasCelebration)
                          .Select(s => new KeyValuePair<int, string>(s.Id, s.Celebration))
                          .ToList();

    /// <summary>
    /// Lista monitorów dla Pilota. Rdzeń nie zna <c>WpfScreenHelper</c> (kompiluje się też pod
    /// Androida), więc ekrany przechodzą przez granicę jako zwykłe dane. Etykieta i słowa
    /// „Ekran"/„(główny)" są te same co w comboboksie zakładki USTAWIENIA, a wymiary podajemy
    /// w FIZYCZNYCH pikselach — operator poznaje monitor po rozdzielczości, nie po DIU.
    /// </summary>
    private IReadOnlyList<PilotSystemSettings.ScreenInfo> BuildScreenList() =>
        Dispatcher.Invoke(() =>
        {
            var screenWord  = TryFindResource("Settings.Screen") as string ?? "Screen";
            var primaryWord = TryFindResource("Settings.ScreenPrimary") as string ?? "(primary)";
            return WpfScreenHelper.Screen.AllScreens
                .Select((s, i) => new PilotSystemSettings.ScreenInfo(
                    i,
                    $"{screenWord} {i + 1}{(s.Primary ? $" {primaryWord}" : "")}  {(int)s.Bounds.Width}×{(int)s.Bounds.Height}",
                    (int)s.Bounds.Width,
                    (int)s.Bounds.Height,
                    s.Primary))
                .ToList();
        });

    /// <summary>
    /// Wykonanie wyniku komendy ustawień systemowych: odpowiedź do nadawcy, broadcast do
    /// wszystkich, przestawienie okna projekcji i odświeżenie zakładki USTAWIENIA. Jedno
    /// miejsce dla obu ścieżek — komendy z tabletu i samoczynnego cofnięcia ekranu.
    /// </summary>
    /// <summary>
    /// Restart procesu — JEDNA ścieżka dla komendy <c>restart_app</c> i dla operacji
    /// nieodwracalnych etapu 4B. Port MUSI zostać zwolniony PRZED startem nowego procesu:
    /// inaczej świeża kopia wchodzi na zajęte gniazdo, jej serwer pilota nie startuje i mini PC
    /// zostaje bez żadnego interfejsu. Komunikat do tabletu wyszedł, zanim tu dotarliśmy
    /// (ack z <c>RemoteControlServer</c>, komunikat terminalny z <c>PilotMaintenance</c>).
    /// </summary>
    private void RestartApplication(string why)
    {
        AppLog.Write("Pilot", $"Restart aplikacji {why}");
        try
        {
            _remoteControl.StopForRestart();
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex) { AppLog.Write("Pilot", $"Restart nieudany: {ex.Message}"); }
    }

    /// <summary>
    /// Budzik bezpiecznika: po <paramref name="seconds"/> pyta maszynę próby, czy jest co cofać.
    /// Jedna metoda dla obu przedmiotów próby (ekran, port) — cofanie ma JEDNO miejsce
    /// (<c>PilotSystemSettings.ExpireTrialAsync</c>), a budzik jednego nie rusza drugiego,
    /// bo każdy ma własny termin.
    /// </summary>
    private void ScheduleSystemSettingsExpiry(DatabaseService db, int seconds) =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            try
            {
                var back = await PilotSystemSettings.ExpireTrialAsync(
                    db, BuildScreenList(), _systemSettingsTrial);
                await ApplySystemSettingsResultAsync(back);
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Cofnięcie próbnej zmiany: {ex.Message}"); }
        });

    private async Task ApplySystemSettingsResultAsync(
        PilotSystemSettings.Result result,
        System.Net.WebSockets.WebSocket? ws = null)
    {
        if (result.Response != null && ws != null)
            await _remoteControl.SendToClientAsync(ws, result.Response);
        if (result.Broadcast != null)
            await _remoteControl.BroadcastJsonAsync(result.Broadcast);

        // Port przeładowujemy DOPIERO TERAZ — przeładowanie zrywa wszystkie połączenia, więc
        // ack i broadcast muszą zdążyć wyjść wcześniej. To samo dotyczy powrotu na stary port
        // po nieudanej próbie (`ApplyPort` niesie wtedy poprzednią wartość).
        if (result.ApplyPort != null)
        {
            int newPort = result.ApplyPort.Value;
            _ = Dispatcher.InvokeAsync(() =>
            {
                try { _remoteControl.ApplyPortFromRemote(newPort); }
                catch (Exception ex) { AppLog.Write("Pilot", $"Zmiana portu serwera: {ex.Message}"); }
            });
        }

        // Odpięcie urządzeń („nowy PIN") tak samo jak port: DOPIERO TERAZ, bo kasacja tokenów
        // rozłącza wszystkich klientów — łącznie z nadawcą, który czeka na ack z nowym PIN-em.
        // PIN przyszedł gotowy z rdzenia (ten sam, który poszedł w acku) — tu się go tylko ustawia.
        if (result.ApplyForgetPin != null)
        {
            string newPin = result.ApplyForgetPin;
            _ = Dispatcher.InvokeAsync(() =>
            {
                try { _remoteControl.ForgetPairedDevicesFromRemote(newPin); }
                catch (Exception ex) { AppLog.Write("Pilot", $"Odpięcie urządzeń: {ex.Message}"); }
            });
        }

        if (!result.Refresh) return;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                // Ekran przestawiamy TYLKO wtedy, gdy projekcja JEST otwarta — zmiana ustawienia
                // nie jest poleceniem „otwórz projekcję" (od tego jest `open_projection`).
                // Sama przeprowadzka idzie istniejącą ścieżką, która re-czyta `projection_screen`
                // i przelicza metryki DPI; drugiego takiego miejsca nie piszemy.
                if (result.ApplyScreen != null && _vm.IsProjectionOpen)
                    await _vm.OpenProjectionFromRemoteAsync();

                // Bez tego zakładka USTAWIENIA kłamałaby o trybie/ekranie/języku po powrocie
                // do trybu dual, a najbliższe „ZAPISZ USTAWIENIA" cofnęłoby zmianę z tabletu.
                // `ApplyExternalSettingsAsync` świadomie nie odpala `Saved`, więc broadcast
                // `display_settings_data` nie poleci przy okazji drugi raz. Wczytuje też
                // diecezję, lekcjonarz, interwał pętli i wygaszony ekran — ale z guardami
                // (`_dioceseLoading` itp.), więc SKUTKI UBOCZNE trzeba odpalić osobno, tym
                // samym zdarzeniem co zmiana w oknie.
                await _szablonVm.ApplyExternalSettingsAsync();

                // Diecezja zmienia obchody → dzień liturgiczny na pasku i podpisy PRZYPIĘTYCH.
                if (result.DioceseChanged) _szablonVm.RaiseDioceseChanged();
                // Wydanie lekcjonarza zmienia TREŚĆ psalmu na projekcji → przeładowanie pieśni.
                if (result.LectionaryChanged) _szablonVm.RaiseLectionaryChanged();
            }
            catch (Exception ex) { AppLog.Write("Pilot", $"Odświeżenie ustawień systemowych: {ex.Message}"); }
        });
    }

    /// <summary>Tekst podsumowania „Przypnij tydzień" (7 dni: data · nazwa — obchód).</summary>
    private string BuildPinWeekSummary(PilotPinWeek.Result result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TryFindResource("PinWeek.Summary.Header") as string ?? "");
        sb.AppendLine();
        foreach (var d in result.Days)
        {
            sb.Append(d.Date.ToString("dd.MM (ddd)", new System.Globalization.CultureInfo("pl-PL")))
              .Append("  ").Append(d.Name);
            if (d.Celebration.Length > 0) sb.Append("  —  ").Append(d.Celebration);
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.Append(TryFindResource("PinWeek.Summary.NewCount") as string ?? "")
          .Append(' ').Append(result.Pinned);
        return sb.ToString();
    }

    private void BtnLitFormularies_Click(object sender, RoutedEventArgs e)
    {
        if (_clockDay == null) return;
        var opts = new List<LitFormOption> { new(_clockDay.SetlistName, _clockDay.SetlistName) };
        opts.AddRange(_clockCelebrations.Select(c =>
            new LitFormOption($"{c.Tytul} · {c.RangaLabel}", c.Tytul)));
        LitFormItems.ItemsSource = opts;
        LitPopup.IsOpen = true;
    }

    private void LitFormItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LitFormOption opt)
        {
            DiocesanCalendarService.SetOverride(_clockDate, opt.Name);
            RefreshLitDay();
        }
        LitPopup.IsOpen = false;
    }

    // Pozycja okna

    private void SaveWindowPosition()
    {
        if (WindowState == WindowState.Minimized) return;
        _ = _db.SaveSettingAsync("window_maximized", (WindowState == WindowState.Maximized).ToString());
        if (WindowState == WindowState.Normal)
        {
            _ = _db.SaveSettingAsync("window_left",   Left.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _ = _db.SaveSettingAsync("window_top",    Top.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _ = _db.SaveSettingAsync("window_width",  Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _ = _db.SaveSettingAsync("window_height", Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private void RestoreWindowPosition()
    {
        var leftStr   = _db.GetSettingSync("window_left");
        var topStr    = _db.GetSettingSync("window_top");
        var widthStr  = _db.GetSettingSync("window_width");
        var heightStr = _db.GetSettingSync("window_height");
        var maxStr    = _db.GetSettingSync("window_maximized");

        if (double.TryParse(leftStr,   System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double left)
         && double.TryParse(topStr,    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double top)
         && double.TryParse(widthStr,  System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double width)
         && double.TryParse(heightStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double height))
        {
            // Sprawdź czy pozycja jest wciąż na którymś z ekranów
            var screens = WpfScreenHelper.Screen.AllScreens;
            bool onScreen = screens.Any(s => s.WorkingArea.Contains(new System.Windows.Point(left + 50, top + 50)));
            if (onScreen)
            {
                Left   = left;
                Top    = top;
                Width  = width;
                Height = height;
            }
            else
            {
                // Ekran zniknął — wyśrodkuj na ekranie głównym
                var primary = WpfScreenHelper.Screen.PrimaryScreen;
                Left = primary.WorkingArea.Left + (primary.WorkingArea.Width - width) / 2;
                Top  = primary.WorkingArea.Top  + (primary.WorkingArea.Height - height) / 2;
                Width  = width;
                Height = height;
            }
        }
        else
        {
            // Pierwsze uruchomienie — wyśrodkuj
            var primary = WpfScreenHelper.Screen.PrimaryScreen;
            Width  = 1280;
            Height = 720;
            Left = primary.WorkingArea.Left + (primary.WorkingArea.Width - Width) / 2;
            Top  = primary.WorkingArea.Top  + (primary.WorkingArea.Height - Height) / 2;
        }

        // W trybie serwerowym okno ma zostać zminimalizowane — zapamiętane „zmaksymalizowane"
        // wyciągnęłoby je z powrotem na projekcję.
        if (maxStr == "True" && AppModeRules.ShouldShowMainWindow(AppMode.Current))
            WindowState = WindowState.Maximized;
    }

    // ─── Tryb serwerowy: powrót do okna dla technika (Ctrl+Alt+Shift+C) ────────
    //
    // Okno jest zminimalizowane i zdjęte z paska zadań, więc bez tego skrótu nie ma jak
    // dostać się do USTAWIEŃ na mini PC (np. żeby wyłączyć tryb serwerowy albo zmienić PIN).
    // Skrót globalny, bo okno nie ma fokusu — zwykły KeyBinding by nie zadziałał.

    private const int HotkeyId = 0xC0DE;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
    private const uint VkC = 0x43;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private System.Windows.Interop.HwndSource? _hotkeySource;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (AppModeRules.ShouldShowMainWindow(AppMode.Current)) return;

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _hotkeySource = System.Windows.Interop.HwndSource.FromHwnd(handle);
        _hotkeySource?.AddHook(HotkeyHook);
        bool ok = RegisterHotKey(handle, HotkeyId, ModControl | ModAlt | ModShift | ModNoRepeat, VkC);
        AppLog.Write("App", ok
            ? "Tryb serwerowy: skrót Ctrl+Alt+Shift+C przywraca okno główne."
            : "Tryb serwerowy: NIE udało się zarejestrować Ctrl+Alt+Shift+C (skrót zajęty przez inny program).");
        Closed += (_, _) =>
        {
            UnregisterHotKey(handle, HotkeyId);
            _hotkeySource?.RemoveHook(HotkeyHook);
        };
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || wParam.ToInt32() != HotkeyId) return IntPtr.Zero;
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Show();
        Activate();
        // Projekcja w trybie serwerowym jest Topmost, więc okno techniczne też musi być —
        // inaczej wróciłoby pod nią i skrót wyglądałby na niedziałający.
        Topmost = true;
        AppLog.Write("App", "Tryb serwerowy: okno główne przywrócone skrótem.");
        handled = true;
        return IntPtr.Zero;
    }

    // Tab switching

    private void TabShow_Click(object sender, RoutedEventArgs e) => ShowPane(PaneShow, TabShow);
    private void TabTemplate_Click(object sender, RoutedEventArgs e) => ShowPane(PaneTemplate, TabTemplate);
    private void TabImport_Click(object sender, RoutedEventArgs e) => ShowPane(PaneImport, TabImport);
    private void TabAbout_Click(object sender, RoutedEventArgs e) => ShowPane(PaneAbout, TabAbout);
    private void TabSupport_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("https://buycoffee.to/marekwojtaszek")
                { UseShellExecute = true });

    private void ShowPane(UIElement pane, Button activeTab)
    {
        // Hide all panes
        PaneShow.Visibility = Visibility.Collapsed;
        PaneTemplate.Visibility = Visibility.Collapsed;
        PaneImport.Visibility = Visibility.Collapsed;
        PaneAbout.Visibility = Visibility.Collapsed;

        // Reset all tab styles
        foreach (Button btn in TabBar.Children)
            btn.Style = (Style)Resources["TabBtn"];

        // Activate selected
        pane.Visibility = Visibility.Visible;
        activeTab.Style = (Style)Resources["TabBtnActive"];

        // Track active tab
        _activeTab = pane == PaneShow      ? "show"
            : pane == PaneTemplate         ? "template"
            : pane == PaneAbout            ? "about"
            : "import";
    }

    private string _activeTab = "show";

    private void HandleSave()
    {
        switch (_activeTab)
        {
            case "template":
                if (_szablonVm.SaveCommand.CanExecute(null))
                    _szablonVm.SaveCommand.Execute(null);
                if (_shortcutsVm.SaveCommand.CanExecute(null))
                    _shortcutsVm.SaveCommand.Execute(null);
                break;
            case "import":
                if (_szablonVm.SaveCommand.CanExecute(null))
                    _szablonVm.SaveCommand.Execute(null);
                break;
            case "show":
                if (_vm.IsInlineEditorOpen && _vm.SaveInlineEditCommand.CanExecute(null))
                    _vm.SaveInlineEditCommand.Execute(null);
                else if (_vm.SaveSetlistCommand.CanExecute(null))
                    _vm.SaveSetlistCommand.Execute(null);
                break;
        }
    }

    private void SetlistSearchPopup_Opened(object sender, EventArgs e)
        => SetlistSearchBox.Focus();

    private void TextItemPopup_Opened(object sender, EventArgs e)
        => TxtTextItemTitle.Focus();

    // Nawigacja strzałką w dół z pola wyszukiwania na listę

    private void SearchBoxShow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down) return;
        if (SongListShow.Items.Count == 0) return;

        // Najpierw zaznaczenie, potem fokus na KONTENERZE wiersza (nie na samym ListBoksie)
        if (SongListShow.SelectedIndex < 0)
            SongListShow.SelectedIndex = 0;
        int index = SongListShow.SelectedIndex;

        SongListShow.UpdateLayout();
        SongListShow.ScrollIntoView(SongListShow.Items[index]);
        SongListShow.UpdateLayout();

        if (SongListShow.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
            container.Focus();
        else
            SongListShow.Focus();

        e.Handled = true;
    }

    // Czy fokus klawiatury znajduje się wewnątrz podanej listy?
    private static bool IsFocusInside(ItemsControl list)
    {
        if (list is null) return false;
        var current = Keyboard.FocusedElement as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, list)) return true;
            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    // Skróty formatowania tekstu (Ctrl+klawisz w edytorze zwrotek)

    private void VerseTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // Tab / Shift+Tab: przejście między polami zwrotek (pomijaj przyciski ↑↓)
        if (e.Key == Key.Tab)
        {
            bool backward = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var itemsControl = FindVisualParent<ItemsControl>(tb);
            if (itemsControl != null)
            {
                var boxes = FindVisualChildren<TextBox>(itemsControl).ToList();
                int idx = boxes.IndexOf(tb);
                int next = backward ? idx - 1 : idx + 1;
                if (next >= 0 && next < boxes.Count)
                {
                    boxes[next].Focus();
                    e.Handled = true;
                    return;
                }
            }
            return;
        }

        // Ctrl+klawisz: wstaw tag formatowania
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

        var keyLabel = Helpers.KeyCaptureHelper.KeyToLabel(e.Key);
        var tag = _szablonVm.TextTags.FirstOrDefault(t =>
        {
            var sk = t.ShortcutKey;
            if (sk.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
                sk = sk["Ctrl+".Length..];
            return string.Equals(sk, keyLabel, StringComparison.OrdinalIgnoreCase);
        });
        if (tag == null) return;

        e.Handled = true;
        InsertTagAroundSelection(tb, tag.Name);
    }

    // Potrójny klik: zaznacz cały akapit (do najbliższych \n)
    private void VerseTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 3 || sender is not TextBox tb) return;

        var pos = tb.GetCharacterIndexFromPoint(e.GetPosition(tb), true);
        if (pos < 0) return;

        var text = tb.Text;
        int start = pos > 0 ? text.LastIndexOf('\n', pos - 1) + 1 : 0;
        int end = text.IndexOf('\n', pos);
        if (end < 0) end = text.Length;

        Dispatcher.InvokeAsync(() => tb.Select(start, end - start));
        e.Handled = true;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T t) return t;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var desc in FindVisualChildren<T>(child)) yield return desc;
        }
    }

    private void OpenShortcuts_Click(object sender, RoutedEventArgs e) => OpenShortcutsPopup();

    private void OpenShortcutsPopup()
    {
        var win = new ShortcutsWindow(_shortcutsVm, this);
        win.ShowDialog();
    }

    private void SaveAllSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_szablonVm.SaveCommand.CanExecute(null))
            _szablonVm.SaveCommand.Execute(null);
        if (_shortcutsVm.SaveCommand.CanExecute(null))
            _shortcutsVm.SaveCommand.Execute(null);
    }

    private void SaveWyglad_Click(object sender, RoutedEventArgs e)
    {
        if (_szablonVm.SaveCommand.CanExecute(null))
            _szablonVm.SaveCommand.Execute(null);
    }

    private static void InsertTagAroundSelection(TextBox tb, string tagName)
    {
        int start = tb.SelectionStart;
        int len = tb.SelectionLength;
        var text = tb.Text;
        var open = $"{{{tagName}}}";
        var close = $"{{/{tagName}}}";

        if (len > 0)
        {
            var selected = text.Substring(start, len);
            tb.Text = text.Substring(0, start) + open + selected + close + text.Substring(start + len);
            tb.SelectionStart = start + open.Length;
            tb.SelectionLength = len;
        }
        else
        {
            tb.Text = text.Substring(0, start) + open + close + text.Substring(start);
            tb.SelectionStart = start + open.Length;
        }
    }
}
