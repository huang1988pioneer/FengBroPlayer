using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FengBroPlayer.Converters;

/// <summary>
/// Maps a 0..1 VU meter level to a vertical height (in px) so bars rise from the
/// bottom of the meter strip. <c>parameter</c> is the strip's full height.
/// </summary>
public sealed class VuHeightConverter : IValueConverter
{
    public static readonly VuHeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value switch
        {
            double d => d,
            float f => f,
            _ => 0.0
        };
        level = Math.Clamp(level, 0, 1);

        var full = 22.0;
        if (parameter is double p) full = p;
        else if (parameter is string sp && double.TryParse(sp, NumberStyles.Any, CultureInfo.InvariantCulture, out var ps)) full = ps;

        // Floor of 2 keeps the "off" bar visible (a thin sliver above the bottom).
        return Math.Max(2, level * full);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
