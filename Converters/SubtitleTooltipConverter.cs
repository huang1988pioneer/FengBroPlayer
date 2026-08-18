using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FengBroPlayer33.Converters;

/// <summary>
/// Converts the <c>HasSubtitle</c> bool to a tooltip string for the CC button.
/// true  → "字幕已開啟 — 點擊變更字幕"
/// false → "開啟字幕 (.srt / .ass / .vtt)"
/// </summary>
public sealed class SubtitleTooltipConverter : IValueConverter
{
    public static readonly SubtitleTooltipConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? "字幕已開啟 — 點擊變更字幕"
            : "開啟字幕 (.srt / .ass / .vtt)";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
