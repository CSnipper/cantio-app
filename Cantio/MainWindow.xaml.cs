using Cantio.Models;
using Cantio.Services;
using Cantio.ViewModels;
using Cantio.Views;
using System.Collections.Generic;
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

    // ── Drag & drop kategorii ──────────────────────────────────────────────

    private CategoryEditorItem? _draggedCategory;
    private Point? _categoryDragStart;

    private void CategoryItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _categoryDragStart = null; return; }

        var pos = e.GetPosition(null);
        if (_categoryDragStart == null) { _categoryDragStart = pos; return; }

        if (Math.Abs(pos.X - _categoryDragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _categoryDragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (sender is FrameworkElement fe && fe.DataContext is CategoryEditorItem item && !item.IsEditing)
        {
            _categoryDragStart = null;
            _draggedCategory = item;
            DragDrop.DoDragDrop(fe, item, DragDropEffects.Move);
        }
    }

    private void CategoryList_Drop(object sender, DragEventArgs e)
    {
        if (_draggedCategory == null) return;
        var target = GetCategoryItemAtPoint(CategoryListBox, e.GetPosition(CategoryListBox));
        if (target != null && target != _draggedCategory)
        {
            var items = _vm.CategoryItems;
            int from = items.IndexOf(_draggedCategory);
            int to = items.IndexOf(target);
            if (from >= 0 && to >= 0)
            {
                items.Move(from, to);
                _ = _vm.SaveCategoryOrderAsync();
            }
        }
        _draggedCategory = null;
    }

    private static CategoryEditorItem? GetCategoryItemAtPoint(ListBox lb, Point pt)
    {
        var element = lb.InputHitTest(pt) as UIElement;
        while (element != null)
        {
            if (lb.ItemContainerGenerator.ItemFromContainer(element) is CategoryEditorItem item)
                return item;
            element = VisualTreeHelper.GetParent(element) as UIElement;
        }
        return null;
    }

    // ── Drag & drop listy zestawu ──────────────────────────────────────────

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

        _szablonVm = new SzablonViewModel(db, _vm.Projection);
        _szablonVm.Saved += () => _vm.RebuildSlides();
        PaneTemplate.DataContext = _szablonVm;
        PaneImport.DataContext = _szablonVm;
        ImportColumn.DataContext = _importVm;
        ImportLogColumn.DataContext = _importVm;

        _shortcutsVm = new ShortcutsViewModel(db, _shortcutService);

        _importVm.SetlistsImported += async () => await _vm.LoadPinnedSetlistsAsync();

        Loaded += async (_, _) =>
        {
            await _vm.InitializeAsync();
            RestoreWindowPosition();
        };
        Closing += (_, _) => SaveWindowPosition();
        KeyDown += _vm.OnKeyDown;
    }

    // ── Pozycja okna ──────────────────────────────────────────────────────────

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

    // ── Tab switching ──────────────────────────────────────────────────────────

    private void TabShow_Click(object sender, RoutedEventArgs e) => ShowPane(PaneShow, TabShow);
    // private void TabCats_Click(object sender, RoutedEventArgs e) => ShowPane(PaneCats, TabCats);
    private void TabTemplate_Click(object sender, RoutedEventArgs e) => ShowPane(PaneTemplate, TabTemplate);
    private void TabImport_Click(object sender, RoutedEventArgs e) => ShowPane(PaneImport, TabImport);

    private void ShowPane(UIElement pane, Button activeTab)
    {
        // Hide all panes
        PaneShow.Visibility = Visibility.Collapsed;
        // PaneCats.Visibility = Visibility.Collapsed;
        PaneTemplate.Visibility = Visibility.Collapsed;
        PaneImport.Visibility = Visibility.Collapsed;

        // Reset all tab styles
        foreach (Button btn in TabBar.Children)
            btn.Style = (Style)Resources["TabBtn"];

        // Activate selected
        pane.Visibility = Visibility.Visible;
        activeTab.Style = (Style)Resources["TabBtnActive"];

        // Track active tab
        _activeTab = pane == PaneShow      ? "show"
            : pane == PaneCats             ? "cats"
            : pane == PaneTemplate         ? "template"
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

    // ── Nawigacja strzałką w dół z pola wyszukiwania na listę ─────────────────

    private void SearchBoxShow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down) return;
        SongListShow.Focus();
        if (SongListShow.Items.Count > 0 && SongListShow.SelectedIndex < 0)
            SongListShow.SelectedIndex = 0;
        e.Handled = true;
    }

    // ── Skróty formatowania tekstu (Ctrl+klawisz w edytorze zwrotek) ──────────

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
            string.Equals(t.ShortcutKey, keyLabel, StringComparison.OrdinalIgnoreCase));
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
