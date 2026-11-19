using TinaClass.App.Common.Theme;
using TKCode.Arc.Themes.Corners;
using Xunit;

namespace TinaClass.App.Tests;

/// <summary>The <c>corners</c> value of arc.theme.json: a name, or a number of zero or more (the scale of every radius).</summary>
public class ThemeFileTests
{
    /// <summary>Writes an arc.theme.json with the given body and loads it.</summary>
    private static ThemeFile LoadJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "tinaclass-theme-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            return ThemeFile.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("\"corners\": \"Default\"", 1.0)]
    [InlineData("\"corners\": \"default\"", 1.0)]
    [InlineData("\"corners\": \"Half\"", 0.5)]
    [InlineData("\"corners\": \"HALF\"", 0.5)]
    [InlineData("\"corners\": \"Square\"", 0.0)]
    [InlineData("\"corners\": 0", 0.0)]
    [InlineData("\"corners\": 0.6", 0.6)]
    [InlineData("\"corners\": 1", 1.0)]
    [InlineData("\"corners\": 1.5", 1.5)]
    [InlineData("\"corners\": 2e-1", 0.2)]
    [InlineData("\"corners\": \"0.6\"", 0.6)]
    [InlineData("\"corners\": \" 0.25 \"", 0.25)]
    [InlineData("\"corners\": \"0\"", 0.0)]
    [InlineData("\"corners\": \"1\"", 1.0)]
    [InlineData("\"corners\": \"3\"", 3.0)]
    [InlineData("\"corners\": \"1e0\"", 1.0)]
    public void Corners_accept_a_name_or_a_number_of_zero_or_more(string line, double factor)
    {
        var file = LoadJson("{ " + line + " }");
        Assert.Empty(file.Problems);
        Assert.Equal(ArcCornerStyle.Scale(factor), file.ParsedCorners);
    }

    [Fact]
    public void Named_corners_are_the_matching_styles()
    {
        Assert.Equal(ArcCornerStyle.Default, LoadJson("{ \"corners\": \"Default\" }").ParsedCorners);
        Assert.Equal(ArcCornerStyle.Square, LoadJson("{ \"corners\": \"Square\" }").ParsedCorners);
        Assert.Equal(ArcCornerStyle.Square, LoadJson("{ \"corners\": 0 }").ParsedCorners);
        Assert.Equal(ArcCornerStyle.Default, LoadJson("{ \"corners\": 1 }").ParsedCorners);
        Assert.Equal(ArcCornerStyle.Default, LoadJson("{ }").ParsedCorners);
        Assert.Equal(ArcCornerStyle.Default, new ThemeFile().ParsedCorners);
    }

    [Theory]
    [InlineData("\"corners\": -0.1")]
    [InlineData("\"corners\": \"-1\"")]
    [InlineData("\"corners\": \"\"")]
    [InlineData("\"corners\": \"round\"")]
    [InlineData("\"corners\": \"NaN\"")]
    [InlineData("\"corners\": \"Infinity\"")]
    [InlineData("\"corners\": \"0,6\"")]
    [InlineData("\"corners\": \"0x1\"")]
    [InlineData("\"corners\": \"1e999\"")]
    [InlineData("\"corners\": 1e999")]
    [InlineData("\"corners\": true")]
    [InlineData("\"corners\": false")]
    [InlineData("\"corners\": [1]")]
    [InlineData("\"corners\": { \"scale\": 1 }")]
    public void Invalid_corners_fall_back_to_default_and_are_reported(string line)
    {
        var file = LoadJson("{ \"accent\": \"#7747FF\", " + line + ", \"tokens\": { \"Sidebar\": { \"ExpandedWidth\": \"240\" } } }");
        Assert.Equal(ArcCornerStyle.Default, file.ParsedCorners);
        var problem = Assert.Single(file.Problems);
        Assert.Contains("corners", problem);
        // The rest of the file still applies.
        Assert.NotNull(file.ParsedAccent);
        Assert.Equal("240", file.Tokens["Sidebar"]["ExpandedWidth"]);
    }

    [Fact]
    public void A_null_corners_value_is_the_default_without_a_problem()
    {
        var file = LoadJson("{ \"corners\": null }");
        Assert.Equal(ArcCornerStyle.Default, file.ParsedCorners);
        Assert.Empty(file.Problems);
    }

    [Fact]
    public void Other_invalid_values_are_reported_too()
    {
        var file = LoadJson("{ \"appearance\": \"Sepia\", \"accent\": \"not a color\", \"corners\": \"Half\" }");
        Assert.Equal(2, file.Problems.Count);
        Assert.Contains(file.Problems, problem => problem.Contains("appearance"));
        Assert.Contains(file.Problems, problem => problem.Contains("accent"));
        Assert.Equal(ArcCornerStyle.Scale(0.5), file.ParsedCorners);
    }

    [Fact]
    public void A_broken_file_gives_the_defaults_and_a_problem()
    {
        var file = LoadJson("{ \"corners\": ");
        Assert.Equal(ArcCornerStyle.Default, file.ParsedCorners);
        Assert.Single(file.Problems);
        // A missing file is not an error: the defaults are the intended look.
        Assert.Empty(ThemeFile.Load(Path.Combine(Path.GetTempPath(), "tinaclass-missing-" + Guid.NewGuid().ToString("N") + ".json")).Problems);
    }

    [Theory]
    [InlineData("0.6", true, 0.6)]
    [InlineData("0", true, 0.0)]
    [InlineData("1", true, 1.0)]
    [InlineData("-0", true, 0.0)]
    [InlineData(".5", true, 0.5)]
    [InlineData("5.", true, 5.0)]
    [InlineData("1,5", false, 1.0)]
    [InlineData("-0.5", false, 1.0)]
    [InlineData("+0.5", true, 0.5)]
    [InlineData("0.5 px", false, 1.0)]
    [InlineData(" ", false, 1.0)]
    public void TryParseCorners_reads_invariant_numbers(string text, bool valid, double factor)
    {
        Assert.Equal(valid, ThemeFile.TryParseCorners(text, out var style));
        Assert.Equal(ArcCornerStyle.Scale(factor), style);
    }

    [Fact]
    public void The_shipped_theme_file_has_no_problems()
    {
        var file = ThemeFile.Load(Path.Combine(AppContext.BaseDirectory, "arc.theme.json"));
        Assert.Empty(file.Problems);
        Assert.Equal(ArcCornerStyle.Scale(1.2), file.ParsedCorners);
        Assert.Equal("spacious", file.ParsedDensity);
    }

    [Theory]
    [InlineData("spacious", "spacious", true)]
    [InlineData("Compact", "compact", true)]
    [InlineData(" comfortable ", "comfortable", true)]
    [InlineData("tiny", "spacious", false)]
    public void Density_is_one_of_three_and_a_wrong_value_falls_back_with_a_problem(string text, string parsed, bool valid)
    {
        var file = LoadJson("{ \"density\": \"" + text + "\" }");
        Assert.Equal(parsed, file.ParsedDensity);
        Assert.Equal(valid, file.Problems.Count == 0);
    }

    [Theory]
    [InlineData("mono", "mono", true)]
    [InlineData("Blue", "blue", true)]
    [InlineData("amber", "amber", true)]
    [InlineData("pink", "mono", false)]
    public void Palette_is_a_named_accent_set_and_a_wrong_name_is_mono_with_a_problem(string text, string parsed, bool valid)
    {
        var file = LoadJson("{ \"palette\": \"" + text + "\" }");
        Assert.Equal(parsed, file.ParsedPalette);
        Assert.Equal(valid, file.Problems.Count == 0);
    }

    [Fact]
    public void A_palette_and_a_density_apply_to_a_theme_without_failing()
    {
        foreach (var palette in new[] { "mono", "blue", "violet", "green", "amber" })
        {
            foreach (var density in new[] { "spacious", "comfortable", "compact" })
            {
                var file = LoadJson("{ \"palette\": \"" + palette + "\", \"density\": \"" + density + "\", \"dark\": { \"\": { \"Accent\": \"#F4F4F5\" } } }");
                Assert.Empty(file.Problems);
                var state = TKCode.Arc.ArcThemeState.Default.With(file.ApplyTo);
                Assert.Equal(density == "spacious" ? null : density == "comfortable" ? "46" : "40",
                    state.TokenOverrides.TryGetValue("Table", out var table) && table.TryGetValue("RowHeight", out var height) ? height : null);
            }
        }
    }
}
