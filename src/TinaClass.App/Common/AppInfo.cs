using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinaClass.App.Common;

/// <summary>
/// The app's brand, from <c>app.json</c> next to the executable (Arc Gallery's generator writes it): the name shown to
/// people, the version and the language to start in. Read once.
/// </summary>
public sealed class AppInfo
{
    /// <summary>The id the app's own Arc package (its text) is registered under.</summary>
    public const string PackageId = "TinaClass.App";

    private static AppInfo? s_current;

    /// <summary>The product name shown in the window title and the sidebar.</summary>
    public string Name { get; set; } = "TinaClass";

    public string Version { get; set; } = "1.0.0";

    /// <summary>The mark in the title bar (template option <c>logoMark</c>): <c>spark</c>, <c>ring</c> or <c>grid</c>. A generic drawn shape, never a third party's logo.</summary>
    public string Logo { get; set; } = "spark";

    /// <summary>The currency of the amounts (template option <c>currency</c>): <c>CNY</c> or <c>USD</c>. See <c>Money.Configure</c>.</summary>
    public string Currency { get; set; } = "CNY";

    /// <summary>The language to start in (<c>en-US</c>, <c>zh-CN</c>, <c>zh-TW</c>); <c>system</c> or empty follows the system.</summary>
    public string Language { get; set; } = "system";

    /// <summary>The settings of this run: app.json, or the defaults when it is missing or unreadable.</summary>
    public static AppInfo Current
    {
        get => s_current ??= Load(Path.Combine(AppContext.BaseDirectory, "app.json"));
        set => s_current = value;
    }

    public static AppInfo Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(path), AppInfoJsonContext.Default.AppInfo) ?? new AppInfo();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken app.json must not stop the app: start with the defaults.
        }
        return new AppInfo();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppInfo))]
internal sealed partial class AppInfoJsonContext : JsonSerializerContext;
