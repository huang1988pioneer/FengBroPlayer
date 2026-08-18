using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FengBroPlayer33.Converters;

public sealed class FavoriteGlyphConverter : IValueConverter
{
    public static readonly FavoriteGlyphConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "♥" : "♡";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
