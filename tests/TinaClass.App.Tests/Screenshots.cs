using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Shell;
using TinaClass.App.Features.Chat;
using TinaClass.App.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using TKCode.Arc;
using TKCode.Arc.Motion;
using Xunit;

namespace TinaClass.App.Tests;

/// <summary>
/// Writes the preview scenes of the gallery (design 14) in light and dark at 1440x900 to the folder in TINACLASS_SCREENSHOTS
/// (<c>&lt;scene&gt;.png</c> and <c>&lt;scene&gt;-dark.png</c>); does nothing without it. The folder must be an absolute path.
/// <c>TINACLASS_SCREENSHOTS=/abs/preview dotnet run --project tests/TinaClass.App.Tests -- -method "*Screenshots*"</c>
/// </summary>
public class Screenshots
{
    /// <summary>The question that makes the sample model tell the export story (see <see cref="SampleChatService"/>).</summary>
    private const string Question = "帮我梳理一下「导出功能」的用户痛点，并给出优先级建议";

    [AvaloniaFact]
    public void Preview_scenes_light_and_dark()
    {
        if (Environment.GetEnvironmentVariable("TINACLASS_SCREENSHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        Assert.True(Path.IsPathRooted(folder), "TINACLASS_SCREENSHOTS must be an absolute folder.");
        Directory.CreateDirectory(folder);
        var language = Environment.GetEnvironmentVariable("TINACLASS_SCREENSHOTS_LANGUAGE") is { Length: > 0 } tag ? tag : "zh-CN";
        foreach (var dark in new[] { false, true })
        {
            Scene(folder, language, dark, "home", window => Ui.Go(AppPages.Home));

            // Chat, held in the middle of the answer: first while it thinks, then while a tool runs, then the finished answer.
            Scene(folder, language, dark, "chat-thinking", window => StartChat(Question, "thinking"));
            Scene(folder, language, dark, "chat-tools", window => StartChat(Question, "tools"));
            Scene(folder, language, dark, "chat-answer", window => StartChat(Question, null));

            Scene(folder, language, dark, "knowledge", window => Ui.Go(AppPages.Knowledge));
            Scene(folder, language, dark, "agents", window => Ui.Go(AppPages.Agents));
            Scene(folder, language, dark, "flow", window => Ui.Go(AppPages.Flow));
            Scene(folder, language, dark, "prompts", window => Ui.Go(AppPages.Prompts));
            Scene(folder, language, dark, "usage", window => Ui.Go(AppPages.Usage));
            Scene(folder, language, dark, "models", window => Ui.Go(AppPages.Models));
            Scene(folder, language, dark, "palette", window =>
            {
                Ui.Go(AppPages.Chat);
                Ui.Shell.OpenPaletteCommand.Execute(null);
            });
            Scene(folder, language, dark, "approve", window =>
            {
                Ui.Go(AppPages.Agents);
                _ = DesktopApp.Services.GetRequiredService<IApprovalReview>().ReviewAsync(window);
            });
        }
    }

    private static void StartChat(string text, string? holdAt)
    {
        // The sample model stops at this phase (see SampleChatService.HoldAt); the window of a scene starts with no hold.
        SampleChatService.HoldAt = holdAt;
        // The scenes run with motion off; while the answer is made the composer shows its glow, still (Reduced), as a picture can only show it.
        ArcTheme.Apply(t => t.MotionLevel(holdAt is null ? ArcMotionLevel.Off : ArcMotionLevel.Reduced));
        Ui.Shell.Hub.Start(text);
        Ui.Settle(60);
    }

    /// <summary>Opens a fresh window in the given language and appearance, runs the scene's steps and saves the picture.</summary>
    private static void Scene(string folder, string language, bool dark, string name, Action<MainWindow> steps)
    {
        var window = Ui.Open(language);
        Ui.Theme(dark);
        steps(window);
        Ui.Settle(40);
        using (var frame = window.CaptureRenderedFrame())
        {
            frame?.Save(Path.Combine(folder, name + (dark ? "-dark" : string.Empty) + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        window.Close();
    }
}
