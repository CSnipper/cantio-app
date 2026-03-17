using System.Windows;
using System.Windows.Controls;

namespace Cantio.Helpers;

public static class ListBoxAutoScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ListBoxAutoScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(ListBox obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ListBox obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;
        if ((bool)e.NewValue)
            listBox.SelectionChanged += OnSelectionChanged;
        else
            listBox.SelectionChanged -= OnSelectionChanged;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem == null) return;
        lb.UpdateLayout();
        var container = lb.ItemContainerGenerator.ContainerFromItem(lb.SelectedItem) as FrameworkElement;
        container?.BringIntoView();
    }
}
