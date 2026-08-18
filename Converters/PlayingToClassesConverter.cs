using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FengBroPlayer33.Converters;

public sealed class PlayingToClassesConverter : IValueConverter
{
    public static readonly PlayingToClassesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "playing" : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
