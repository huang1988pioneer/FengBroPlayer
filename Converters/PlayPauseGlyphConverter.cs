using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FengBroPlayer.Converters;

public sealed class PlayPauseGlyphConverter : IValueConverter
{
    public static readonly PlayPauseGlyphConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "❚❚" : "▶";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
