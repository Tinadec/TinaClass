using TKCode.Arc.Localization;

namespace TinaClass.App.Common.Sample;

/// <summary>
/// The sample data's "today" and a few words in the app's language. Sample records are dated relative to today, so the
/// data looks the same whenever the app runs; tests and screenshots pin <see cref="NowProvider"/>.
/// </summary>
public static class SampleClock
{
    /// <summary>"Now" for the whole app; tests and screenshots replace it.</summary>
    public static Func<DateTimeOffset> NowProvider { get; set; } = () => DateTimeOffset.Now;

    public static DateTimeOffset Now => NowProvider();

    /// <summary>A moment <paramref name="daysAgo"/> days before today, at the given time.</summary>
    public static DateTimeOffset At(int daysAgo, int hour, int minute)
    {
        var day = Now.LocalDateTime.Date.AddDays(-daysAgo).AddHours(hour).AddMinutes(minute);
        return new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
    }

    /// <summary>Sample text in the app's language.</summary>
    public static string Pick(string en, string zhCn, string zhTw) => ArcLanguage.Current.Value switch
    {
        "zh-CN" => zhCn,
        "zh-TW" => zhTw,
        _ => en,
    };
}
