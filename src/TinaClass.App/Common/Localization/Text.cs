using System.Globalization;
using TKCode.Arc.Localization;

namespace TinaClass.App.Common.Localization;

/// <summary>The app's text in the current language, for code (XAML uses <c>{l:AppLangResource Key}</c>).</summary>
public static class Text
{
    public static string Get(AppLangResourceKind kind) => AppLangResources.Get(kind);

    public static string Format(AppLangResourceKind kind, params object?[] args) =>
        string.Format(Culture, AppLangResources.Get(kind), args);

    /// <summary>The culture that formats dates and numbers: the app's language.</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(ArcLanguage.Current.Value);
}
