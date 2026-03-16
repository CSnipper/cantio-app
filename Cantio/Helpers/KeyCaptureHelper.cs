using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cantio.Helpers;

/// <summary>
/// Zamienia TextBox w pole przechwytywania klawisza.
/// Użytkownik klika pole, wciska dowolny klawisz → tekst = nazwa klawisza.
/// Esc/Backspace/Delete → czyści.
/// </summary>
public static class KeyCaptureHelper
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(KeyCaptureHelper),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(TextBox tb) => (bool)tb.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(TextBox tb, bool value) => tb.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue)
        {
            tb.IsReadOnly = true;
            tb.Cursor = Cursors.Hand;
            tb.PreviewKeyDown += OnKeyDown;
            tb.GotFocus += OnGotFocus;
            tb.LostFocus += OnLostFocus;
        }
        else
        {
            tb.PreviewKeyDown -= OnKeyDown;
            tb.GotFocus -= OnGotFocus;
            tb.LostFocus -= OnLostFocus;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // Ignoruj same modyfikatory
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                  or Key.LeftAlt or Key.RightAlt or Key.System or Key.LWin or Key.RWin
                  or Key.Tab)
            return;

        if (e.Key is Key.Escape or Key.Back or Key.Delete)
        {
            tb.Text = string.Empty;
            e.Handled = true;
            return;
        }

        // Zachowaj tylko jeden znak — litery i cyfry; resztę też przyjmij
        tb.Text = KeyToLabel(e.Key);
        e.Handled = true;
    }

    private static void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.SelectAll();
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Select(0, 0);
    }

    /// <summary>Zamienia Key na krótką, czytelną etykietę.</summary>
    public static string KeyToLabel(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),            // A–Z
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),  // 0–9
        >= Key.NumPad0 and <= Key.NumPad9 => $"N{(int)(key - Key.NumPad0)}", // numpad
        >= Key.F1 and <= Key.F12 => key.ToString(),         // F1–F12
        Key.Space => "Space",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "+",
        _ => key.ToString()
    };
}
