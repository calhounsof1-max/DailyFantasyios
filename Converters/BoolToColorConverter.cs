using System.Globalization;

namespace DailyFantasyMAUI.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var colors = parameter?.ToString()?.Split('|');
            if (colors?.Length != 2) return Colors.Transparent;
            if (value is not bool b) return Color.FromArgb(colors[1]);
            bool active = b;
            return Color.FromArgb(active ? colors[0] : colors[1]);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
