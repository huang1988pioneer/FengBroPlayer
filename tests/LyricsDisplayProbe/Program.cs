using Avalonia;
using FengBroPlayer.Controls;

AppBuilder.Configure<FengBroPlayer.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .SetupWithoutStarting();

static void AssertEqual(string expected, string actual, string caseName)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{caseName}: expected '{expected.Replace("\n", "\\n")}', " +
            $"got '{actual.Replace("\n", "\\n")}'.");
}

AssertEqual(
    "一二三\n四五",
    LyricTextFormatter.BreakAtPreviousWhitespace("一二三 四五", [4]),
    "wrap at the previous space");

AssertEqual(
    "前  後",
    LyricTextFormatter.BreakAtPreviousWhitespace("前  後", []),
    "preserve spaces when no wrap is needed");

AssertEqual(
    "一二三四",
    LyricTextFormatter.BreakAtPreviousWhitespace("一二三四", [2]),
    "leave unbreakable text unchanged");

AssertEqual(
    "一\n二\n三\n四",
    LyricTextFormatter.BreakAtPreviousWhitespace("一 二 三 四", [2, 4, 6]),
    "apply multiple wrapped breaks");

AssertEqual(
    "一 二\n三 四",
    LyricTextFormatter.BreakAtPreviousWhitespace("一 二\n三 四", [4]),
    "preserve explicit line breaks and their spaces");

var source = "一二三 四五";
var wrappingControl = new LyricTextBlock
{
    Text = source,
    FontSize = 18,
    MaxWidth = 80,
    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
    TextTrimming = Avalonia.Media.TextTrimming.None
};
wrappingControl.Measure(new Size(80, double.PositiveInfinity));
AssertEqual("一二三\n四五", wrappingControl.Text, "format the measured control text");

var singleLineControl = new LyricTextBlock
{
    Text = "前  後",
    FontSize = 18,
    MaxWidth = 240,
    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
    TextTrimming = Avalonia.Media.TextTrimming.None
};
singleLineControl.Measure(new Size(240, double.PositiveInfinity));
AssertEqual("前  後", singleLineControl.Text, "preserve spaces in a single measured line");

Console.WriteLine("Lyrics formatter regression probe passed.");
