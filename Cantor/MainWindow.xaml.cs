using System.Windows;
using System.Windows.Controls;
using Cantor.Services;
using Cantor.ViewModels;

namespace Cantor;

public partial class MainWindow : Window
{
    private readonly DisplayViewModel _vm;
    private readonly ImportViewModel _importVm;
    private readonly SzablonViewModel _szablonVm;
    private readonly SongEditorViewModel _songEditorVm;
    private readonly SetlistViewModel _setlistVm;

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