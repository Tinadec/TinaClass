using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.Themes.Corners;

namespace TinaClass.App.Common.Theme;

/// <summary>
/// <c>arc.theme.json</c>: the app's look as data, so Arc Gallery's generator (and you) can change it without touching code.
/// <code>
/// { "appearance": "System" | "Light" | "Dark",
///   "accent": "#0562EF",
///   "corners": "Default" | "Half" | "Square" | 0.6 | "0.6",    // a number >= 0 is a scale: 1 = Default, 0.5 = Half, 0 = Square
///   "palette": "mono" | "blue" | "violet" | "green" | "amber",   // mono: the accent of the file; the others: a light and a dark accent set
///   "density": "spacious" | "comfortable" | "compact",   // row height of tables and the padding of the figure cards; "tokens" below win
///   "tokens": { "": { "RadiusControl": "12" }, "Switch": { "TrackWidth": "50" } },   // both appearances
///   "light":  { "": { "Background": "#FBFBFA" } },                                    // light only
///   "dark":   { "": { "Background": "#101012" } } }                                   // dark only
/// </code>
/// <c>""</c> is the global ArcDesign identity; other keys are a control's token identity. Token values are strings, as in
/// <c>ArcThemeBuilder.Token</c>. A missing or broken file gives Arc's defaults, and so does a value that is not valid (an unknown
/// appearance, a color that does not parse, corners that are neither a name nor a number of zero or more): the rest of the file
/// still applies and the reason is in <see cref="Problems"/> (also written to <see cref="Trace"/>).
/// </summary>
public sealed class ThemeFile
{
    private static ThemeFile? s_current;

    public string Appearance { get; set; } = "System";

    public string? Accent { get; set; }

    /// <summary>"Default", "Half" or "Square" (any case), or a number of zero or more written as a JSON number or a numeric string
    /// (the scale of every corner radius: 1 is Default, 0 is Square). Read it through <see cref="ParsedCorners"/>.</summary>
    [JsonConverter(typeof(CornersJsonConverter))]
    public string Corners { get; set; } = "Default";

    /// <summary>"spacious" (Arc's rows, the look of the design), "comfortable" or "compact": how much room tables and figure cards take. Read it through <see cref="ParsedDensity"/>.</summary>
    public string Density { get; set; } = "spacious";

    /// <summary>"mono" (the accent and the light/dark tokens of the file), or a named accent set that replaces them. Read it through <see cref="ParsedPalette"/>.</summary>
    public string Palette { get; set; } = "mono";

    public Dictionary<string, Dictionary<string, string>> Tokens { get; set; } = new();

    public Dictionary<string, Dictionary<string, string>> Light { get; set; } = new();

    public Dictionary<string, Dictionary<string, string>> Dark { get; set; } = new();

    /// <summary>What is wrong with the values of the file this instance was loaded from (empty when all is valid).</summary>
    [JsonIgnore]
    public List<string> Problems { get; } = [];

    public static ThemeFile Current
    {
        get => s_current ??= Load(Path.Combine(AppContext.BaseDirectory, "arc.theme.json"));
        set => s_current = value;
    }

    public static ThemeFile Load(string path)
    {
        var file = new ThemeFile();
        try
        {
            if (File.Exists(path))
            {
                file = JsonSerializer.Deserialize(File.ReadAllText(path), ThemeFileJsonContext.Default.ThemeFile) ?? new ThemeFile();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken theme file falls back to Arc's defaults.
            file.Problems.Add($"{path} could not be read ({exception.Message}); Arc's defaults are used.");
        }
        file.Validate();
        foreach (var problem in file.Problems)
        {
            Trace.TraceWarning("arc.theme.json: " + problem);
        }
        return file;
    }

    /// <summary>Fills <see cref="Problems"/> with the values that are not valid (each one falls back to its default).</summary>
    public void Validate()
    {
        if (!Enum.TryParse<ArcAppearance>(Appearance, ignoreCase: true, out _))
        {
            Problems.Add($"appearance \"{Appearance}\" is not System, Light or Dark; System is used.");
        }
        if (Accent is not null && !Color.TryParse(Accent, out _))
        {
            Problems.Add($"accent \"{Accent}\" is not a color; Arc's accent is used.");
        }
        if (Density?.Trim().ToLowerInvariant() is not ("spacious" or "comfortable" or "compact"))
        {
            Problems.Add($"density \"{Density}\" is not spacious, comfortable or compact; spacious is used.");
        }
        if (!Palettes.ContainsKey(Palette?.Trim().ToLowerInvariant() ?? ""))
        {
            Problems.Add($"palette \"{Palette}\" is not mono, blue, violet, green or amber; mono is used.");
        }
        if (!TryParseCorners(Corners, out _))
        {
            Problems.Add($"corners \"{Corners}\" is not Default, Half, Square or a number of 0 or more; Default is used.");
        }
    }

    /// <summary>The appearance the file asks for.</summary>
    public ArcAppearance ParsedAppearance => Enum.TryParse<ArcAppearance>(Appearance, ignoreCase: true, out var value) ? value : ArcAppearance.System;

    /// <summary>The accent color, or null for Arc's default.</summary>
    public Color? ParsedAccent => Accent is not null && Color.TryParse(Accent, out var color) ? color : null;

    /// <summary>The corner style the file asks for; Default when the value is not valid.</summary>
    public ArcCornerStyle ParsedCorners => TryParseCorners(Corners, out var style) ? style : ArcCornerStyle.Default;

    /// <summary>
    /// Reads a corners value: "Default", "Half" or "Square" (any case), or a finite number of zero or more in invariant format
    /// ("0.6", "1", "1.5", "2e-1"), which is <see cref="ArcCornerStyle.Scale"/>. Anything else (a negative number, NaN, an empty
    /// string, other words) is not valid.
    /// </summary>
    public static bool TryParseCorners(string? text, out ArcCornerStyle style)
    {
        style = ArcCornerStyle.Default;
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return text is null; // a missing value is the default; an empty string is not valid
        }
        switch (value.ToLowerInvariant())
        {
            case "default":
                return true;
            case "half":
                style = ArcCornerStyle.Scale(0.5);
                return true;
            case "square":
                style = ArcCornerStyle.Square;
                return true;
        }
        if (double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var factor) && double.IsFinite(factor) && factor >= 0)
        {
            style = ArcCornerStyle.Scale(factor);
            return true;
        }
        return false;
    }

    /// <summary>Applies the file to the theme being built.</summary>
    public void ApplyTo(ArcThemeBuilder theme)
    {
        theme.Appearance(ParsedAppearance);
        var named = Palettes[ParsedPalette];
        if (named.Length == 6 && Color.TryParse(named[0], out var paletteAccent))
        {
            theme.Accent(paletteAccent);
        }
        else if (ParsedAccent is { } accent)
        {
            theme.Accent(accent);
        }
        var corners = ParsedCorners;
        if (corners != ArcCornerStyle.Default)
        {
            theme.Corners(corners);
        }
        foreach (var (identity, values) in DensityTokens(ParsedDensity).Concat(Tokens))
        {
            foreach (var (name, value) in values)
            {
                if (identity.Length == 0)
                {
                    theme.Token(name, value);
                }
                else
                {
                    theme.Token(identity, name, value);
                }
            }
        }
        var light = WithAccent(Light, ParsedPalette, dark: false);
        var dark = WithAccent(Dark, ParsedPalette, dark: true);
        if (HasAny(light) || HasAny(dark))
        {
            theme.Preset(ArcThemePreset.Create("app.theme-file", preset =>
            {
                preset.ForLight(builder => AddTokens(builder, light));
                preset.ForDark(builder => AddTokens(builder, dark));
            }));
        }
    }

    [JsonIgnore]
    public string ParsedPalette => Palette?.Trim().ToLowerInvariant() is { } name && Palettes.ContainsKey(name) ? name : "mono";

    // Accent, strong accent and subtle accent of a named palette, light then dark (AccentForeground follows from the accent).
    private static readonly Dictionary<string, string[]> Palettes = new()
    {
        ["mono"] = [],
        ["blue"] = ["#0562EF", "#0451C8", "#E6F0FE", "#6AA6FF", "#8DBBFF", "#16243F"],
        ["violet"] = ["#6D3BF5", "#5A2BD6", "#EFE9FE", "#A98BFF", "#C0A9FF", "#241A45"],
        ["green"] = ["#0A7A50", "#086541", "#E2F4EC", "#4FD39B", "#7BE3B6", "#12302A"],
        ["amber"] = ["#B45F06", "#984F05", "#FBEFD9", "#F2B84B", "#F7CB77", "#3A2A0E"],
    };

    /// <summary>The appearance's tokens with the accent of the palette (when it is not mono) and an AccentForeground that is readable on the accent.</summary>
    private static Dictionary<string, Dictionary<string, string>> WithAccent(Dictionary<string, Dictionary<string, string>> tokens, string palette, bool dark)
    {
        var copy = tokens.ToDictionary(pair => pair.Key, pair => new Dictionary<string, string>(pair.Value));
        if (!copy.TryGetValue("", out var global))
        {
            copy[""] = global = new Dictionary<string, string>();
        }
        if (Palettes[palette] is { Length: 6 } colors)
        {
            var at = dark ? 3 : 0;
            global["Accent"] = colors[at];
            global["AccentStrong"] = colors[at + 1];
            global["AccentSubtle"] = colors[at + 2];
        }
        if (global.TryGetValue("Accent", out var accent) && Color.TryParse(accent, out var color))
        {
            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
            global["AccentForeground"] = luminance > 0.55 ? "#0B0B0C" : "#FAFAFA";
        }
        return copy;
    }

    /// <summary>The density, or "spacious" for a value that is not one of the three (the reason is in <see cref="Problems"/>).</summary>
    [JsonIgnore]
    public string ParsedDensity => Density?.Trim().ToLowerInvariant() is "compact" or "comfortable" ? Density.Trim().ToLowerInvariant() : "spacious";

    /// <summary>The tokens a density sets: spacious keeps Arc's rows (52 px) and the design's cards; the others lower the table rows and the figure cards' padding.</summary>
    private static IEnumerable<KeyValuePair<string, Dictionary<string, string>>> DensityTokens(string density)
    {
        if (density == "spacious")
        {
            yield return new("MetricCard", new() { ["CardPadding"] = "18,16" });
        }
        else if (density == "comfortable")
        {
            yield return new("Table", new() { ["RowHeight"] = "46", ["HeaderHeight"] = "40" });
            yield return new("MetricCard", new() { ["CardPadding"] = "16,14" });
        }
        else if (density == "compact")
        {
            yield return new("Table", new() { ["RowHeight"] = "40", ["HeaderHeight"] = "36" });
            yield return new("MetricCard", new() { ["CardPadding"] = "14,10", ["NarrowCardPadding"] = "12,10" });
        }
    }

    private static bool HasAny(Dictionary<string, Dictionary<string, string>> tokens) => tokens.Values.Any(values => values.Count > 0);

    private static void AddTokens(ArcThemePresetBuilder builder, Dictionary<string, Dictionary<string, string>> tokens)
    {
        foreach (var (identity, values) in tokens)
        {
            foreach (var (name, value) in values)
            {
                if (identity.Length == 0)
                {
                    builder.Token(name, value);
                }
                else
                {
                    builder.Token(identity, name, value);
                }
            }
        }
    }
}

/// <summary>Reads <c>corners</c> as a string, a number or anything else (kept as text that is not valid, so only that value falls back).</summary>
internal sealed class CornersJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString() ?? "";
            case JsonTokenType.Number:
                // Kept as invariant text, so 0.6 and "0.6" are read the same way later.
                return reader.TryGetDouble(out var number) && double.IsFinite(number)
                    ? number.ToString("R", CultureInfo.InvariantCulture)
                    : "not a finite number";
            default:
                var kind = reader.TokenType.ToString();
                reader.Skip();
                return kind; // true, false, an object or an array: not valid, reported by Validate
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ThemeFile))]
internal sealed partial class ThemeFileJsonContext : JsonSerializerContext;
