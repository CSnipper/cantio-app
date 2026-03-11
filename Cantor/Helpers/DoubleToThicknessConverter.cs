using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Cantor.Helpers;

public class DoubleToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new Thickness((double)value, 0, (double)value, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}