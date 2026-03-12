using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cantio.Helpers;

/// <summary>
/// Attached behavior dla TextBox z wartością liczbową.
/// Użycie: helpers:NumericBox.IsEnabled="True"
/// Opcjonalnie: helpers:NumericBox.Step="1" helpers:NumericBox.Min="0" helpers:NumericBox.Max="999"
/// </summary>
public static class NumericBox
{
    // ── IsEnabled ────────────────────────────────────────────────────────

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(NumericBox),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(TextBox tb) => (bool)tb.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(TextBox tb, bool value) => tb.SetValue(IsEnabledProperty, value);

    // ── Step ─────────────────────────────────────────────────────────────

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.RegisterAttached(
            "Step", typeof(double), typeof(NumericBox),
            new PropertyMetadata(1.0));

    public static double GetStep(TextBox tb) => (double)tb.GetValue(StepProperty);
    public static void SetStep(TextBox tb, double value) => tb.SetValue(StepProperty, value);

    // ── Min ──────────────────────────────────────────────────────────────

    public static readonly DependencyProperty MinProperty =
        DependencyProperty.RegisterAttached(
            "Min", typeof(double), typeof(NumericBox),
            new PropertyMetadata(double.MinValue));

    public static double GetMin(TextBox tb) => (double)tb.GetValue(MinProperty);
    public static void SetMin(TextBox tb, double value) => tb.SetValue(MinProperty, value);

    // ── Max ──────────────────────────────────────────────────────────────

    public static readonly DependencyProperty MaxProperty =
        DependencyProperty.RegisterAttached(
            "Max", typeof(double), typeof(NumericBox),
            new PropertyMetadata(double.MaxValue));

    public static double GetMax(TextBox tb) => (double)tb.GetValue(MaxProperty);
    public static void SetMax(TextBox tb, double value) => tb.SetValue(MaxProperty, value);

    // ── Decimals ─────────────────────────────────────────────────────────

    public static readonly DependencyProperty DecimalsProperty =
        DependencyProperty.RegisterAttached(
            "Decimals", typeof(int), typeof(NumericBox),
            new PropertyMetadata(0));

    public static int GetDecimals(TextBox tb) => (int)tb.GetValue(DecimalsProperty);
    public static void SetDecimals(TextBox tb, int value) => tb.SetValue(DecimalsProperty, value);

    // ── Hooks ────────────────────────────────────────────────────────────

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue)
        {
            tb.PreviewKeyDown += OnKeyDown;
            tb.PreviewMouseWheel += OnMouseWheel;
        }
        else
        {
            tb.PreviewKeyDown -= OnKeyDown;
            tb.PreviewMouseWheel -= OnMouseWheel;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (e.Key == Key.Up) { Change(tb, +1); e.Handled = true; }
        if (e.Key == Key.Down) { Change(tb, -1); e.Handled = true; }
    }

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not TextBox tb) return;
        Change(tb, e.Delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    private static void Change(TextBox tb, int direction)
    {
        if (!double.TryParse(tb.Text.Replace(",", "."),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out double current)) return;

        double step = GetStep(tb);
        double min = GetMin(tb);
        double max = GetMax(tb);
        int decimals = GetDecimals(tb);

        double next = Math.Clamp(current + direction * step, min, max);
        tb.Text = decimals == 0
            ? ((int)Math.Round(next)).ToString()
            : next.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);

        // Wymuś aktualizację bindingu
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
}
