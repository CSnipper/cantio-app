using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Cantio.Helpers;

[ValueConversion(typeof(string), typeof(string))]
public partial class StripTagsConverter : IValueConverter
{
    [GeneratedRegex(@"\{/?(\w+)\}")]
    private static partial Regex TagPattern();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s ? TagPattern().Replace(s, string.Empty) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
