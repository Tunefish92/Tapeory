using Tapeory.Api.Rendering;

namespace Tapeory.Api.Tests.Unit;

public sealed class TextFitterTests
{
    // A predictable "font": every character is half the font size wide.
    private static float Measure(string text, float size) => text.Length * size * 0.5f;

    [Fact]
    public void None_KeepsSizeAndLines_EvenWhenTooBig()
    {
        var fitted = TextFitter.Fit("a very long line of text", 5, 2, 4, TextFitMode.None, Measure);

        Assert.Equal(4, fitted.FontSize);
        Assert.Equal(["a very long line of text"], fitted.Lines);
    }

    [Fact]
    public void Shrink_KeepsFullSize_WhenTextAlreadyFits()
    {
        var fitted = TextFitter.Fit("abc", 100, 10, 4, TextFitMode.Shrink, Measure);

        Assert.Equal(4, fitted.FontSize);
        Assert.Equal(["abc"], fitted.Lines);
    }

    [Fact]
    public void Shrink_ReducesTheSize_UntilTheLongestLineFitsTheWidth()
    {
        // 20 chars * 0.5 * size <= 20 => size <= 2
        var fitted = TextFitter.Fit("12345678901234567890", 20, 100, 8, TextFitMode.Shrink, Measure);

        Assert.InRange(fitted.FontSize, 1.99f, 2.001f);
        Assert.Single(fitted.Lines);
    }

    [Fact]
    public void Shrink_KeepsTypedLineBreaks_AndFitsTheHeight()
    {
        // 3 lines * 1.2 * size <= 6 => size <= 1.667
        var fitted = TextFitter.Fit("a\nb\nc", 100, 6, 10, TextFitMode.Shrink, Measure);

        Assert.Equal(["a", "b", "c"], fitted.Lines);
        Assert.InRange(fitted.FontSize, 1.65f, 1.668f);
    }

    [Fact]
    public void Wrap_BreaksAtSpaces_WithoutCuttingWords()
    {
        // At size 2 each char is 1 wide; the box is 11 wide and tall enough for 3 lines.
        var fitted = TextFitter.Fit("strawberry jam with vanilla", 11, 2 * 1.2f * 3, 2, TextFitMode.Wrap, Measure);

        Assert.Equal(2, fitted.FontSize);
        Assert.Equal(["strawberry", "jam with", "vanilla"], fitted.Lines);
        Assert.All(fitted.Lines, line => Assert.DoesNotContain(line, l => l == '-'));
    }

    [Fact]
    public void Wrap_ShrinksAWordThatIsWiderThanTheBox_InsteadOfSplittingIt()
    {
        var fitted = TextFitter.Fit("Supercalifragilistic", 10, 100, 4, TextFitMode.Wrap, Measure);

        Assert.Equal(["Supercalifragilistic"], fitted.Lines);
        Assert.True(Measure(fitted.Lines[0], fitted.FontSize) <= 10.001f);
    }

    [Fact]
    public void Wrap_ShrinksWhenTheWrappedLinesAreTooTallForTheBox()
    {
        var text = "one two three four five six seven eight";
        var fitted = TextFitter.Fit(text, 10, 3, 2, TextFitMode.Wrap, Measure);

        Assert.True(fitted.FontSize < 2);
        Assert.True(fitted.Lines.Count * fitted.FontSize * TextFitter.LineHeightFactor <= 3.001f);
        Assert.All(fitted.Lines, line => Assert.True(Measure(line, fitted.FontSize) <= 10.001f));
        // Every word survives intact, in order.
        Assert.Equal(text, string.Join(" ", fitted.Lines));
    }

    [Fact]
    public void Fit_NeverGoesBelowTheMinimumSize()
    {
        var fitted = TextFitter.Fit(new string('x', 10_000), 1, 1, 10, TextFitMode.Shrink, Measure);

        Assert.Equal(TextFitter.MinFontSize, fitted.FontSize, 3);
    }

    [Theory]
    [InlineData("shrink", TextFitMode.Shrink)]
    [InlineData("wrap", TextFitMode.Wrap)]
    [InlineData("none", TextFitMode.None)]
    [InlineData(null, TextFitMode.None)]
    [InlineData("bogus", TextFitMode.None)]
    public void ParseMode_ReadsKnownValues_AndDefaultsToNone(string? value, TextFitMode expected)
    {
        Assert.Equal(expected, TextFitter.ParseMode(value));
    }
}
