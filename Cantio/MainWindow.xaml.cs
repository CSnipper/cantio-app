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

        // Skróty projekcji działają zawsze — niezależnie od fokusu listy pieśni
        _vm.HandleKey(e.Key, e.KeyboardDevice.Modifiers);
        e.Handled = true;
        base.OnPreviewKeyDown(e);
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
        _vm.ConfirmRequested = msg =>
            MessageBox.Show(msg, "Cantio", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
        DataContext = _vm;

        _importVm = new ImportViewModel(db);

        _szablonVm = new SzablonViewModel(db, _vm.Projection, _vm);
        _szablonVm.Saved += () => _vm.RebuildSlides();
        _szablonVm.DioceseChanged += RefreshLitDay;
        PaneTemplate.DataContext = _szablonVm;
        PaneImport.DataContext = _szablonVm;
        ImportColumn.DataContext = _importVm;
        ImportLogColumn.DataContext = _importVm;

        _shortcutsVm = new ShortcutsViewModel(db, _shortcutService);

        _importVm.SetlistsImported += async () => await _vm.LoadPinnedSetlistsAsync();

        PaneAbout.DataContext = _aboutVm;

        _remoteControl = new RemoteControlViewModel(db);
        PilotPanel.DataContext = _remoteControl;

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
        _remoteControl.SetlistClearRequested += () =>
            _ = Dispatcher.InvokeAsync(() => _vm.ClearSetlistCommand.Execute(null));

        _remoteControl.SetlistRestoreRequested += songIds =>
            _ = Dispatcher.InvokeAsync(async () =>
            {
                _vm.ClearSetlistCommand.Execute(null);
                foreach (var songId in songIds)
                {
                    var song = await db.GetSongWithVersesAsync(songId);
                    if (song != null) _vm.AddToSetlistCommand.Execute(song);
                }
            });

        _remoteControl.GetSetlistsRequested += async ws =>
        {
            try
            {
                var summaries = await db.GetSetlistSummariesAsync();
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type     = "setlists_data",
                    setlists = summaries.Select(s => new { id = s.Id, name = s.Name, group = s.Group ?? "", songCount = s.SongCount, updatedAt = s.UpdatedAt })
                });
                await _remoteControl.SendToClientAsync(ws, json);
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
                    songs     = detail.Value.Songs.Select(s => new { id = s.SongId, title = s.Title })
                });
                await _remoteControl.SendToClientAsync(ws, json);
            }
            catch { }
        };

        _remoteControl.SetlistSyncPushRequested += async (ws, rawJson) =>
        {
            try
            {
                var doc       = System.Text.Json.JsonDocument.Parse(rawJson);
                var root      = doc.RootElement;
                int? desktopId = root.TryGetProperty("desktopId", out var dEl) && dEl.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? dEl.GetInt32() : null;
                var name      = root.GetProperty("name").GetString() ?? "";
                var updatedAt = root.GetProperty("updatedAt").GetInt64();
                var songIds   = root.GetProperty("songs").EnumerateArray()
                    .Select(s => s.GetProperty("id").GetInt32()).ToArray();
                var assignedId = await db.CreateOrUpdateSetlistFromPilotAsync(desktopId, name, updatedAt, songIds);
                var ackJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type      = "setlist_sync_ack",
                    desktopId = assignedId,
                    name      = name
                });
                await _remoteControl.SendToClientAsync(ws, ackJson);
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
                    _vm.ClearSetlistCommand.Execute(null);
                    foreach (var item in setlist.Items.OrderBy(i => i.Position))
                    {
                        if (item.SongId.HasValue)
                        {
                            var song = await db.GetSongWithVersesAsync(item.SongId.Value);
                            if (song != null) _vm.AddToSetlistCommand.Execute(song);
                        }
                    }
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
            try
            {
                var (total, items) = await db.GetSongsForSyncAsync(offset, limit);
                var json = JsonSerializer.Serialize(new
                {
                    type = "songs_data",
                    offset,
                    total,
                    items = items.Select(s => new
                    {
                        id         = s.Id,
                        title      = s.Title,
                        number     = s.Number,
                        author     = s.Author ?? "",
                        categoryId = s.CategoryId,
                        parts      = s.Verses
                            .OrderBy(v => v.Position)
                            .Select(v => new { type = v.Type, text = v.Text })
                    })
                });
                await _remoteControl.SendToClientAsync(ws, json);
            }
            catch { }
        };
        _remoteControl.ClientConnected += async ws =>
        {
            try
            {
                var cats = await db.GetCategoriesAsync();
                var catsJson = JsonSerializer.Serialize(new
                {
                    type       = "categories_data",
                    categories = cats.Select(c => new { id = c.Id, name = c.Name, number = c.Number })
                });
                await _remoteControl.SendToClientAsync(ws, catsJson);
                await BroadcastCurrentStateToAsync(ws);
                await BroadcastSetlistStateToAsync(ws);
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
            try { await _remoteControl.BroadcastAsync(text, title, index, total, isBlank, slides); }
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
            var json = JsonSerializer.Serialize(new
            {
                type = "slide", text, songTitle = title, index, total, isBlank, slides
            });
            await _remoteControl.SendToClientAsync(ws, json);
        }

        async Task BroadcastSetlistState()
        {
            var songs = _vm.SetlistItems
                .Select(item => (id: item.SongId ?? 0, title: item.Song?.Title ?? ""))
                .ToList<(int id, string title)>();
            var activeIndex = _vm.SelectedSetlistItem != null
                ? _vm.SetlistItems.IndexOf(_vm.SelectedSetlistItem) : -1;
            try { await _remoteControl.BroadcastSetlistAsync(songs, activeIndex); }
            catch { }
        }

        async Task BroadcastSetlistStateToAsync(WebSocket ws)
        {
            var songs = _vm.SetlistItems
                .Select(item => new { id = item.SongId ?? 0, title = item.Song?.Title ?? "" })
                .ToList();
            var activeIndex = _vm.SelectedSetlistItem != null
                ? _vm.SetlistItems.IndexOf(_vm.SelectedSetlistItem) : -1;
            var json = JsonSerializer.Serialize(new { type = "setlist", activeIndex, songs });
            await _remoteControl.SendToClientAsync(ws, json);
        }

        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName is nameof(DisplayViewModel.CurrentSlideIndex)
                               or nameof(DisplayViewModel.ScreenBlanked)
                               or nameof(DisplayViewModel.SlideList))
                await BroadcastCurrentState();
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
            await _aboutVm.CheckAndPromptAsync();

            var updateTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromHours(1) };
            updateTimer.Tick += async (_, _) => await _aboutVm.CheckAndPromptAsync();
            updateTimer.Start();
        };
        Closing += (_, _) => { SaveWindowPosition(); _remoteControl.Dispose(); };
        KeyDown += _vm.OnKeyDown;

        InitClock();
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

        if (maxStr == "True")
            WindowState = WindowState.Maximized;
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

    // Nawigacja strzałką w dół z pola wyszukiwania na listę

    private void SearchBoxShow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down) return;
        SongListShow.Focus();
        if (SongListShow.Items.Count > 0 && SongListShow.SelectedIndex < 0)
            SongListShow.SelectedIndex = 0;
        e.Handled = true;
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
