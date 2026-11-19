using System.Globalization;
using TinaClass.App.Common.Settings;
using TKCode.Arc.Localization;

namespace TinaClass.App.Common.Localization;

/// <summary>
/// The languages the app offers. Arc's packages have text for each of them; to add one, add a Localization/&lt;tag&gt;.xlf
/// file with every unit translated and list it here.
/// </summary>
public static class AppLanguages
{
    public static IReadOnlyList<(LanguageTag Tag, string Name)> All { get; } =
    [
        (LanguageTags.EnUS, "English"),
        (LanguageTags.ZhCN, "简体中文"),
        (LanguageTags.ZhTW, "繁體中文"),
    ];

    public static IEnumerable<LanguageTag> Supported => All.Select(language => language.Tag);

    /// <summary>The language to start in: chosen in the app, else app.json's, else the system's, else English.</summary>
    public static LanguageTag Initial
    {
        get
        {
            foreach (var candidate in new[] { UserSettings.Current.Language, AppInfo.Current.Language })
            {
                if (LanguageTag.TryParse(candidate, out var tag) && Supported.Contains(tag))
                {
                    return tag;
                }
            }
            var system = CultureInfo.CurrentUICulture.Name;
            if (system.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return system.Contains("Hant", StringComparison.OrdinalIgnoreCase) || system.EndsWith("TW", StringComparison.OrdinalIgnoreCase)
                       || system.EndsWith("HK", StringComparison.OrdinalIgnoreCase)
                    ? LanguageTags.ZhTW
                    : LanguageTags.ZhCN;
            }
            return LanguageTags.EnUS;
        }
    }

    /// <summary>The display name of a language tag.</summary>
    public static string NameOf(LanguageTag tag) => All.FirstOrDefault(language => language.Tag == tag).Name ?? tag.Value;
}
