using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FengBroPlayer33.Helpers;

namespace FengBroPlayer33.Converters;

public sealed class HueToBrushConverter : IValueConverter
{
    public static readonly HueToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => CoverBrush.GradientFromHue(value?.ToString());

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
