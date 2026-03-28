using System.Globalization;
using System.Windows.Data;

namespace Cantio.Helpers;

public class WidthToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double w ? w * 9.0 / 16.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
