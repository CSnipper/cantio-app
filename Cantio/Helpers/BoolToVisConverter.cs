// Helpers/BoolToVisConverter.cs
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Cantio.Helpers;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}

[ValueConversion(typeof(string), typeof(string))]
public class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s ? Path.GetFileName(s) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Mapuje <see cref="Cantio.Services.Devices.DevicePowerState"/> na kolor kropki stanu.</summary>
public class PowerStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush On = new((Color)ColorConverter.ConvertFromString("#3d8b40")!);
    private static readonly SolidColorBrush Off = new((Color)ColorConverter.ConvertFromString("#9aa3b8")!);
    private static readonly SolidColorBrush Unknown = new((Color)ColorConverter.ConvertFromString("#2a3347")!);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            Cantio.Services.Devices.DevicePowerState.On => On,
            Cantio.Services.Devices.DevicePowerState.Off => Off,
            _ => Unknown
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Mapuje <see cref="Cantio.Services.Devices.DevicePowerState"/> na kolory przycisku urządzenia
/// na pasku górnym. Parametr: <c>bg</c> (tło) lub <c>fg</c> (tekst).
/// Włączone = złoty akcent, wyłączone = szary, nieznany = przygaszony pośredni.
/// </summary>
public class PowerStateToButtonBrushConverter : IValueConverter
{
    private static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex)!);

    private static readonly SolidColorBrush OnBg = B("#c9a84c");
    private static readonly SolidColorBrush OnFg = B("#1b2030");
    private static readonly SolidColorBrush OffBg = B("#252d3d");
    private static readonly SolidColorBrush OffFg = B("#9aa3b8");
    private static readonly SolidColorBrush UnknownBg = B("#4a4230");
    private static readonly SolidColorBrush UnknownFg = B("#c9bd93");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool fg = string.Equals(parameter as string, "fg", StringComparison.OrdinalIgnoreCase);
        return value switch
        {
            Cantio.Services.Devices.DevicePowerState.On => fg ? OnFg : OnBg,
            Cantio.Services.Devices.DevicePowerState.Off => fg ? OffFg : OffBg,
            _ => fg ? UnknownFg : UnknownBg
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(string), typeof(Color))]
public class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try { return (Color)ColorConverter.ConvertFromString(value?.ToString() ?? "#ffffff"); }
        catch { return Colors.White; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}