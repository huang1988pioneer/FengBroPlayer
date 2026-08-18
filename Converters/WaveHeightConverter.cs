using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FengBroPlayer.Converters;

public sealed class WaveHeightConverter : IValueConverter
{
    public static readonly WaveHeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
            return Math.Max(4, d * 30);
        if (value is float f)
            return Math.Max(4, f * 30);
        return 8.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
