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

    public MainWindow(DatabaseService db)
    {
        InitializeComponent();

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

        Loaded += async (_, _) => await _vm.InitializeAsync();
        KeyDown += _vm.OnKeyDown;
    }

    // ── Tab switching ──────────────────────────────────────────────────────────

    private void TabShow_Click(object sender, RoutedEventArgs e) => ShowPane(PaneShow, TabShow);
    private void TabSongs_Click(object sender, RoutedEventArgs e) => ShowPane(PaneSongs, TabSongs);
    private void TabCats_Click(object sender, RoutedEventArgs e) => ShowPane(PaneCats, TabCats);
    private void TabSets_Click(object sender, RoutedEventArgs e) => ShowPane(PaneSets, TabSets);
    private void TabTemplate_Click(object sender, RoutedEventArgs e) => ShowPane(PaneTemplate, TabTemplate);
    private void TabImport_Click(object sender, RoutedEventArgs e) => ShowPane(PaneImport, TabImport);

    private void ShowPane(UIElement pane, Button activeTab)
    {
        // Hide all panes
        PaneShow.Visibility = Visibility.Collapsed;
        PaneSongs.Visibility = Visibility.Collapsed;
        PaneCats.Visibility = Visibility.Collapsed;
        PaneSets.Visibility = Visibility.Collapsed;
        PaneTemplate.Visibility = Visibility.Collapsed;
        PaneImport.Visibility = Visibility.Collapsed;

        // Reset all tab styles
        foreach (Button btn in TabBar.Children)
            btn.Style = (Style)Resources["TabBtn"];

        // Activate selected
        pane.Visibility = Visibility.Visible;
        activeTab.Style = (Style)Resources["TabBtnActive"];
    }
}