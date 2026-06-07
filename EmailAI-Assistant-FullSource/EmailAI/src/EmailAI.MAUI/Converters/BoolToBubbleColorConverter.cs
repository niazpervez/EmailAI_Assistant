using System.Globalization;

namespace EmailAI.MAUI.Converters;

public sealed class BoolToBubbleColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Color.FromArgb("#4F8EF7") : Color.FromArgb("#2D3548");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
