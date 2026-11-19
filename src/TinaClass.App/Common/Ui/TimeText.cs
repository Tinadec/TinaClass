using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Sample;

namespace TinaClass.App.Common.Ui;

/// <summary>Short, human times for lists: "14:32" today, "Yesterday", then the date.</summary>
public static class TimeText
{
    public static string Short(DateTimeOffset moment)
    {
        var now = SampleClock.Now.LocalDateTime.Date;
        var local = moment.LocalDateTime;
        if (local.Date == now)
        {
            return local.ToString("HH:mm", Text.Culture);
        }
        if (local.Date == now.AddDays(-1))
        {
            return Text.Get(AppLangResourceKind.Yesterday);
        }
        return local.ToString(local.Year == now.Year ? "M/d" : "yyyy/M/d", Text.Culture);
    }
}
