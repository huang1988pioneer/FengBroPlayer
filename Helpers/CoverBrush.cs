using Avalonia.Media;

namespace FengBroPlayer33.Helpers;

public static class CoverBrush
{
    public static IBrush FromHue(string? hueText, double saturation = 0.45, double lightness = 0.38)
    {
        if (!double.TryParse(hueText, out var hue))
            hue = 250;

        // Simple HSL → RGB for album / video cover placeholders
        var c = (1 - System.Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - System.Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;
        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new SolidColorBrush(Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255)));
    }

    public static IBrush GradientFromHue(string? hueText)
    {
        if (!double.TryParse(hueText, out var hue))
            hue = 250;
        var a = FromHue(hueText, 0.55, 0.32) as SolidColorBrush;
        var b = FromHue(((hue + 40) % 360).ToString("0"), 0.5, 0.48) as SolidColorBrush;
        return new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(a?.Color ?? Colors.SlateBlue, 0),
                new GradientStop(b?.Color ?? Colors.MediumPurple, 1)
            ]
        };
    }
}
