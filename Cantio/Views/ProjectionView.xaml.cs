using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Cantio.ViewModels;

namespace Cantio.Views;

public partial class ProjectionView : UserControl
{
    public static readonly DependencyProperty IsPreviewModeProperty =
        DependencyProperty.Register(nameof(IsPreviewMode), typeof(bool), typeof(ProjectionView),
            new PropertyMetadata(false, (d, _) => ((ProjectionView)d).SyncOperatorVisibility()));

    public bool IsPreviewMode
    {
        get => (bool)GetValue(IsPreviewModeProperty);
        set => SetValue(IsPreviewModeProperty, value);
    }

    private ProjectionViewModel? _vm;

    public ProjectionView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = DataContext as ProjectionViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
            SyncOperatorVisibility();
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectionViewModel.IsOperatorOverride)
                           or nameof(ProjectionViewModel.IsBlank)
                           or nameof(ProjectionViewModel.IsImageSlide))
            SyncOperatorVisibility();
    }

    private void SyncOperatorVisibility()
    {
        if (MainTextBlock == null || OperatorTextBlock == null || BlankOverlay == null) return;
        bool isImageSlide = _vm?.IsImageSlide ?? false;
        bool showOperator = IsPreviewMode && (_vm?.IsOperatorOverride ?? false);
        MainTextBlock.Visibility     = (showOperator || isImageSlide) ? Visibility.Collapsed : Visibility.Visible;
        OperatorTextBlock.Visibility = (showOperator && !isImageSlide) ? Visibility.Visible   : Visibility.Collapsed;
        if (IsPreviewMode)
            BlankOverlay.Visibility = Visibility.Collapsed;
    }
}
