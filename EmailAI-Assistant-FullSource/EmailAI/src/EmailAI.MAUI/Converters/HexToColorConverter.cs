using System.Globalization;

namespace EmailAI.MAUI.Converters;

public sealed class HexToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Colors.Gray;

        return Color.FromArgb(hex.StartsWith('#') ? hex : $"#{hex}");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
