using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.TextFormatting;

namespace FengBroPlayer.Controls;

/// <summary>
/// Displays a lyric line without changing its whitespace unless the available
/// width actually causes wrapping. When wrapping is needed, an explicit line
/// break is appended after the whitespace immediately before each wrapped
/// segment.
/// </summary>
public sealed class LyricTextBlock : TextBlock
{
    private string _sourceText = string.Empty;
    private double _lastMeasuredWidth = double.NaN;
    private bool _needsFormatting = true;
    private bool _isApplyingDisplayText;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            if (!_isApplyingDisplayText)
            {
                _sourceText = change.GetNewValue<string?>() ?? string.Empty;
                _needsFormatting = true;
                _lastMeasuredWidth = double.NaN;
            }

            return;
        }

        // Font, wrapping, alignment, and other layout properties can change
        // which whitespace is the correct break point.
        _needsFormatting = true;
        _lastMeasuredWidth = double.NaN;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = GetEffectiveWidth(availableSize.Width);
        if (!_needsFormatting && AreSameWidth(width, _lastMeasuredWidth))
            return base.MeasureOverride(availableSize);

        SetDisplayText(_sourceText);
        var measured = base.MeasureOverride(availableSize);

        _lastMeasuredWidth = width;
        _needsFormatting = false;

        if (!CanPreprocess(width) || TextLayout is not { TextLines.Count: > 1 } layout)
            return measured;

        var wrappedLineStarts = GetAutomaticallyWrappedLineStarts(layout);
        var displayText = LyricTextFormatter.BreakAtPreviousWhitespace(
            _sourceText,
            wrappedLineStarts);

        if (string.Equals(displayText, _sourceText, StringComparison.Ordinal))
            return measured;

        SetDisplayText(displayText);
        return base.MeasureOverride(availableSize);
    }

    private double GetEffectiveWidth(double availableWidth)
    {
        var width = availableWidth;
        if (double.IsNaN(width) || double.IsInfinity(width))
            width = MaxWidth;
        else if (!double.IsInfinity(MaxWidth))
            width = Math.Min(width, MaxWidth);

        return width;
    }

    private bool CanPreprocess(double width)
        => TextWrapping != Avalonia.Media.TextWrapping.NoWrap
           && !double.IsNaN(width)
           && !double.IsInfinity(width)
           && width > 0;

    private void SetDisplayText(string text)
    {
        if (string.Equals(Text, text, StringComparison.Ordinal))
            return;

        _isApplyingDisplayText = true;
        try
        {
            Text = text;
        }
        finally
        {
            _isApplyingDisplayText = false;
        }
    }

    private static bool AreSameWidth(double left, double right)
        => !double.IsNaN(left)
           && !double.IsNaN(right)
           && Math.Abs(left - right) < 0.1;

    private static IReadOnlyList<int> GetAutomaticallyWrappedLineStarts(TextLayout layout)
    {
        var starts = new List<int>();
        var lines = layout.TextLines;
        for (var index = 1; index < lines.Count; index++)
        {
            // An explicit newline is already intentional; only preprocess
            // line transitions caused by the available width.
            if (lines[index - 1].NewLineLength > 0)
                continue;

            starts.Add(lines[index].FirstTextSourceIndex);
        }

        return starts;
    }
}

/// <summary>Applies display-only line breaks to measured lyric lines.</summary>
public static class LyricTextFormatter
{
    /// <summary>
    /// Appends a newline after the breakable whitespace immediately before each
    /// wrapped line. All characters, including the whitespace, remain as-is.
    /// </summary>
    public static string BreakAtPreviousWhitespace(
        string text,
        IReadOnlyList<int> wrappedLineStarts)
    {
        if (string.IsNullOrEmpty(text) || wrappedLineStarts.Count == 0)
            return text;

        var breakPositions = new HashSet<int>();
        foreach (var lineStart in wrappedLineStarts)
        {
            var whitespaceIndex = FindPreviousBreakableWhitespace(text, lineStart - 1);
            if (whitespaceIndex >= 0)
                breakPositions.Add(whitespaceIndex);
        }

        if (breakPositions.Count == 0)
            return text;

        var result = new StringBuilder(text.Length + breakPositions.Count);
        for (var index = 0; index < text.Length; index++)
        {
            result.Append(text[index]);
            if (breakPositions.Contains(index))
                result.Append('\n');
        }

        return result.ToString();
    }

    private static int FindPreviousBreakableWhitespace(string text, int index)
    {
        for (var current = Math.Min(index, text.Length - 1); current >= 0; current--)
        {
            var character = text[current];
            if (character is '\r' or '\n')
                return -1;
            if (character is ' ' or '\t' or '\u3000')
                return current;
        }

        return -1;
    }
}
