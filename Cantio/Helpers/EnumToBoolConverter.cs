using System.Globalization;
using System.Windows.Data;

namespace Cantio.Helpers
{
    [ValueConversion(typeof(Enum), typeof(bool))]
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            return value.ToString()
                        .Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true && parameter != null)
            {
                if (targetType.IsEnum)
                    return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);

                return parameter; // dla właściwości string — zwróć parametr bezpośrednio
            }

            return Binding.DoNothing;
        }
    }
}