using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinaClass.App.Common.Api;

/// <summary>
/// Where the app's data comes from, from <c>app.json</c> next to the executable (the same file as <see cref="AppInfo"/>, which
/// ignores these two keys): <c>"dataSource": "api"</c> signs in to the API at <c>"apiBaseUrl"</c>; <c>"sample"</c> keeps the
/// built-in sample data, offline. Read once.
/// </summary>
public sealed class ApiSettings
{
    private static ApiSettings? s_current;

    /// <summary><c>api</c> or <c>sample</c>.</summary>
    public string DataSource { get; set; } = "sample";

    public string ApiBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>Whether the app talks to the API (otherwise it uses the sample data).</summary>
    [JsonIgnore]
    public bool UsesApi => string.Equals(DataSource, "api", StringComparison.OrdinalIgnoreCase);

    /// <summary>The API's host and port, for messages ("localhost:5080").</summary>
    [JsonIgnore]
    public string Host => Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri) ? uri.Authority : ApiBaseUrl;

    /// <summary>The settings of this run: app.json, or the defaults when it is missing or unreadable (tests set it).</summary>
    public static ApiSettings Current
    {
        get => s_current ??= Load(Path.Combine(AppContext.BaseDirectory, "app.json"));
        set => s_current = value;
    }

    public static ApiSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(path), ApiSettingsJsonContext.Default.ApiSettings) ?? new ApiSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken app.json must not stop the app: start on the sample data.
        }
        return new ApiSettings();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ApiSettings))]
internal sealed partial class ApiSettingsJsonContext : JsonSerializerContext;
