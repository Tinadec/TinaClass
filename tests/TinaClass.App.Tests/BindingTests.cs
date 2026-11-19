using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Shell;
using Xunit;

namespace TinaClass.App.Tests;

/// <summary>Every page of every layout binds without a warning: a mistyped path or a missing property shows up here, not as an empty spot on a screen.</summary>
public class BindingTests
{
    private sealed class Capture : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area is LogArea.Binding or LogArea.Property or LogArea.Control or LogArea.Visual;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Lines.Add($"{area}: {messageTemplate}");

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Lines.Add($"{area}: {Format(messageTemplate, propertyValues)}");

        private static string Format(string template, object?[] values)
        {
            var text = template;
            foreach (var value in values)
            {
                var open = text.IndexOf('{', StringComparison.Ordinal);
                var close = text.IndexOf('}', StringComparison.Ordinal);
                if (open < 0 || close < open)
                {
                    break;
                }
                text = text[..open] + value + text[(close + 1)..];
            }
            return text;
        }
    }

    internal static List<string> Visit(Func<MainWindow> open)
    {
        var capture = new Capture();
        var previous = Logger.Sink;
        Logger.Sink = capture;
        try
        {
            var window = open();
            foreach (var language in new[] { "en-US", "zh-CN", "zh-TW" })
            {
                Ui.Shell.SetLanguageCommand.Execute(language);
                Ui.Settle(4);
                foreach (var page in AppPages.All)
                {
                    Ui.Go(page.Key);
                    Ui.Settle(10);
                }
            }
            window.Close();
        }
        finally
        {
            Logger.Sink = previous;
        }
        return capture.Lines;
    }

    [AvaloniaFact]
    public void Every_page_of_the_chosen_layout_binds_cleanly_in_the_three_languages()
    {
        var lines = Visit(() => Ui.Open());

        Assert.True(lines.Count == 0, string.Join(Environment.NewLine, lines.Distinct().Take(20)));
    }
}
