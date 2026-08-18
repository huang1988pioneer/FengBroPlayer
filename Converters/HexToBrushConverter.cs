using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FengBroPlayer33.Converters;

public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            try
            {
                return new SolidColorBrush(Color.Parse(s));
            }
            catch
            {
                // fall through
            }
        }
        return Brushes.MediumPurple;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
