using System.Text.Json;
using System.Text.Json.Serialization;
using TinaClass.App.Common.Settings;

namespace TinaClass.App.Common.Sample;

/// <summary>
/// Where the sample (offline) mode keeps the changes a person makes, so a restart keeps them: model keys and switches,
/// and anything else a sample service wants to save. Stored as <c>sample-store.json</c> next to <c>settings.json</c>;
/// a dictionary per feature, serialized on write. The API mode never reads it.
/// </summary>
public static class SampleStore
{
    private static readonly string s_path = Path.Combine(
        Path.GetDirectoryName(UserSettings.DefaultPath) ?? ".",
        "sample-store.json");

    private static SampleStoreFile? s_file;
    private static readonly object s_lock = new();

    /// <summary>For tests and previews: an in-memory bag instead of the file.</summary>
    public static Func<ISettingsStore>? Store { get; set; }

    /// <summary>Drops the in-memory cache, so the next read loads the store again (tests simulate a restart).</summary>
    public static void Reset()
    {
        lock (s_lock)
        {
            s_file = null;
        }
    }

    /// <summary>Reads a feature's saved state, or an empty dictionary when none is stored.</summary>
    public static Dictionary<string, JsonElement> Read(string feature)
    {
        var file = Load();
        return file.Features.TryGetValue(feature, out var data)
            ? data
            : [];
    }

    /// <summary>Replaces a feature's saved state and writes the file.</summary>
    public static void Write(string feature, Dictionary<string, JsonElement> data)
    {
        lock (s_lock)
        {
            var file = Load();
            file.Features[feature] = data;
            Save(file);
        }
    }

    private static SampleStoreFile Load()
    {
        lock (s_lock)
        {
            if (s_file is not null)
            {
                return s_file;
            }
            var json = (Store?.Invoke() ?? new FileSettingsStore(s_path)).Read();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    s_file = JsonSerializer.Deserialize(json, SampleStoreJsonContext.Default.SampleStoreFile);
                }
                catch (JsonException)
                {
                    s_file = null;
                }
            }
            s_file ??= new SampleStoreFile();
            return s_file;
        }
    }

    private static void Save(SampleStoreFile file)
    {
        s_file = file;
        var json = JsonSerializer.Serialize(file, SampleStoreJsonContext.Default.SampleStoreFile);
        (Store?.Invoke() ?? new FileSettingsStore(s_path)).Write(json);
    }
}

/// <summary>The on-disk shape: a version plus a dictionary of feature-name → feature-state.</summary>
internal sealed class SampleStoreFile
{
    public int Version { get; set; } = 1;

    public Dictionary<string, Dictionary<string, JsonElement>> Features { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SampleStoreFile))]
internal sealed partial class SampleStoreJsonContext : JsonSerializerContext;
