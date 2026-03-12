using Cantio.ViewModels;
using System.Windows;
using WpfScreenHelper;

namespace Cantio.Views
{
    public partial class ProjectionWindow : Window
    {
        public ProjectionWindow(ProjectionViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        /// <summary>
        /// Przenieś okno na wybrany monitor i rozciągnij na cały ekran.
        /// </summary>
        /// <param name="screenIndex">Indeks monitora (0 = główny, 1 = drugi, itd.)</param>
        public void MoveToSecondaryScreen(int screenIndex)
        {
            var screens = Screen.AllScreens.ToList();
            var target = screenIndex < screens.Count ? screens[screenIndex] : screens.Last();
            var area = target.WpfBounds; // nie WpfWorkingArea — pełny ekran

            WindowState = WindowState.Normal;
            Left = area.Left;
            Top = area.Top;
            Width = area.Width;
            Height = area.Height;

            // Wymuś pełny ekran po pozycjonowaniu
            Dispatcher.BeginInvoke(() =>
            {
                Left = area.Left;
                Top = area.Top;
                Width = area.Width;
                Height = area.Height;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void SetBlanked(bool blanked)
        {
            if (DataContext is ProjectionViewModel vm)
                vm.IsBlank = blanked;
        }

        public void Refresh() { }
    }
}
