using Cantio.Services;
using Cantio.ViewModels;
using System.Windows;
using System.Windows.Media;
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

            // Wymuś pozycję po pozycjonowaniu (Width/Height ustawiane przez DisplayViewModel po odczycie właściwego DPI)
            Dispatcher.BeginInvoke(() =>
            {
                Left = area.Left;
                Top = area.Top;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Kompensacja skali Windows: zawartość okna jest układana w FIZYCZNYCH pikselach ekranu
        /// (tych samych, w których <c>SlideLayoutService</c> liczy podział na slajdy), a następnie
        /// skalowana do rozmiaru okna w DIU. Bez tego przy 150% płótno miało 1280×720 przy
        /// niezmienionym rozmiarze czcionki — mniej tekstu na slajd i dwa razy więcej slajdów.
        ///
        /// Przy skali 100% (<see cref="ProjectionMetrics.IsIdentity"/>) NIC nie jest ustawiane:
        /// siatka wraca do rozciągania na okno, dokładnie jak przed tą poprawką.
        /// </summary>
        public void ApplyDpiScale(ProjectionMetrics m)
        {
            if (m.IsIdentity)
            {
                RootScale.Width = double.NaN;
                RootScale.Height = double.NaN;
                RootScale.LayoutTransform = Transform.Identity;
                return;
            }

            RootScale.Width = m.LayoutWidth;
            RootScale.Height = m.LayoutHeight;
            RootScale.LayoutTransform = new ScaleTransform(m.ScaleX, m.ScaleY);
        }

        public void SetBlanked(bool blanked)
        {
            if (DataContext is ProjectionViewModel vm)
                vm.IsBlank = blanked;
        }

        public void Refresh() { }
    }
}
