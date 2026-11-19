using System.Globalization;

namespace TinaClass.App.Common.Ui;

/// <summary>File sizes for lists: "4.6 MB", "38 KB".</summary>
public static class SizeText
{
    public static string Format(long bytes)
    {
        var culture = CultureInfo.InvariantCulture;
        return bytes switch
        {
            >= 1_000_000_000 => (bytes / 1_000_000_000.0).ToString("0.0", culture) + " GB",
            >= 1_000_000 => (bytes / 1_000_000.0).ToString("0.0", culture) + " MB",
            >= 1_000 => (bytes / 1_000.0).ToString("0", culture) + " KB",
            _ => bytes.ToString(culture) + " B",
        };
    }
}
