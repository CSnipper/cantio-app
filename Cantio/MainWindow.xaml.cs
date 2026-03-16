using Cantio.Models;
using Cantio.Services;
using Cantio.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cantio;

public partial class MainWindow : Window
{
    private readonly DisplayViewModel _vm;
    private readonly ImportViewModel _importVm;
    private readonly SzablonViewModel _szablonVm;
    private readonly SongEditorViewModel _songEditorVm;
    private readonly SetlistViewModel _setlistVm;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Nie przechwytuj gdy fokus jest na polu tekstowym
        if (e.OriginalSource is TextBox || e.OriginalSource is RichTextBox)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        if (e.Key == Key.Delete)
        {
            if (_activeTab == "sets" && _setlistVm.SelectedSetlist != null)
            {
                _setlistVm.DeleteSetlistCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (_activeTab == "songs" && _songEditorVm.EditingSong != null)
            {
                _songEditorVm.DeleteSongCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        _vm.HandleKey(e.Key, e.KeyboardDevice.Modifiers);
        e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

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

        _vm = new DisplayViewModel(db, new ProjectionViewModel());
        DataContext = _vm;

        _importVm = new ImportViewModel(db);
        PaneImport.DataContext = _importVm;

        _songEditorVm = new SongEditorViewModel(db);
        PaneSongs.DataContext = _songEditorVm;

        _setlistVm = new SetlistViewModel(db);
        PaneSets.DataContext = _setlistVm;

        _szablonVm = new SzablonViewModel(db, _vm.Projection);
        _szablonVm.Saved += () => _vm.RebuildSlides();
        PaneTemplate.DataContext = _szablonVm;

        _importVm.SetlistsImported += async () => await _setlistVm.LoadAsync();
        _setlistVm.PinnedChanged += async () => await _vm.LoadPinnedSetlistsAsync();

        Loaded += async (_, _) => await _vm.InitializeAsync();
        KeyDown += _vm.OnKeyDown;
    }

    // ── Tab switching ──────────────────────────────────────────────────────────

    private void TabShow_Click(object sender, RoutedEventArgs e) => ShowPane(PaneShow, TabShow);
    private void TabSongs_Click(object sender, RoutedEventArgs e) => ShowPane(PaneSongs, TabSongs);
    // private void TabCats_Click(object sender, RoutedEventArgs e) => ShowPane(PaneCats, TabCats);
    private void TabSets_Click(object sender, RoutedEventArgs e) => ShowPane(PaneSets, TabSets);
    private void TabTemplate_Click(object sender, RoutedEventArgs e) => ShowPane(PaneTemplate, TabTemplate);
    private void TabImport_Click(object sender, RoutedEventArgs e) => ShowPane(PaneImport, TabImport);

    private void ShowPane(UIElement pane, Button activeTab)
    {
        // Hide all panes
        PaneShow.Visibility = Visibility.Collapsed;
        PaneSongs.Visibility = Visibility.Collapsed;
        // PaneCats.Visibility = Visibility.Collapsed;
        PaneSets.Visibility = Visibility.Collapsed;
        PaneTemplate.Visibility = Visibility.Collapsed;
        PaneImport.Visibility = Visibility.Collapsed;

        // Reset all tab styles
        foreach (Button btn in TabBar.Children)
            btn.Style = (Style)Resources["TabBtn"];

        // Activate selected
        pane.Visibility = Visibility.Visible;
        activeTab.Style = (Style)Resources["TabBtnActive"];

        // Track active tab
        _activeTab = pane == PaneShow ? "show"
            : pane == PaneSongs ? "songs"
            : pane == PaneCats ? "cats"
            : pane == PaneSets ? "sets"
            : pane == PaneTemplate ? "template"
            : "import";
    }

    private int _setsEditorDragFromIndex = -1;

    private void SetlistEditorItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe)
        {
            if (fe.DataContext is SetlistItem item)
            {
                _setsEditorDragFromIndex = _setlistVm.Items.IndexOf(item);
                if (_setsEditorDragFromIndex >= 0)
                    DragDrop.DoDragDrop(fe, item, DragDropEffects.Move);
            }
        }
    }

    private void SetlistEditorItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SetlistItem target)
        {
            int toIndex = _setlistVm.Items.IndexOf(target);
            if (_setsEditorDragFromIndex >= 0 && toIndex >= 0 && _setsEditorDragFromIndex != toIndex)
            {
                _setlistVm.Items.Move(_setsEditorDragFromIndex, toIndex);
                _setsEditorDragFromIndex = -1;
            }
        }
    }

    private string _activeTab = "show";

    // ── Skróty formatowania tekstu (Ctrl+klawisz w edytorze zwrotek) ──────────

    private void VerseTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (sender is not TextBox tb) return;

        var keyLabel = Helpers.KeyCaptureHelper.KeyToLabel(e.Key);
        var tag = _szablonVm.TextTags.FirstOrDefault(t =>
            string.Equals(t.ShortcutKey, keyLabel, StringComparison.OrdinalIgnoreCase));
        if (tag == null) return;

        e.Handled = true;
        InsertTagAroundSelection(tb, tag.Name);
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