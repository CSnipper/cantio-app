using System.Windows;
using WpfScreenHelper;
using Cantor.ViewModels;

namespace Cantor.Views
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

            // NIE ustawiaj WindowState.Maximized — zostaw Normal z pełnymi wymiarami
        }

        public void SetBlanked(bool blanked)
        {
            if (DataContext is ProjectionViewModel vm)
                vm.IsBlank = blanked;
        }

        public void Refresh() { }
    }
}
