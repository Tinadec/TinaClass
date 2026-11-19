using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TinaClass.App.Common.Ui;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Chat;
using TinaClass.Contracts.Chat;
using TKCode.Arc;
using TKCode.Arc.Motion;
using Xunit;

namespace TinaClass.App.Tests.Features.Chat;

/// <summary>The chat page in the window: an answer streams into the page and the right wing, the seams hold a state for the screenshots.</summary>
public class ChatPageTests
{
    private const string Question = "帮我梳理一下「导出功能」的用户痛点，并给出优先级建议";

    [AvaloniaFact]
    public void A_question_from_home_starts_a_conversation_and_the_answer_fills_the_inspector()
    {
        var window = Ui.Open("zh-CN");
        var session = Ui.Service<ChatSession>();
        Ui.Shell.Hub.Start(Question);
        Assert.True(Ui.Until(() => session.Inspector.Status == GenerationStatus.Done, 200), "the answer finished");

        Assert.Equal(AppPages.Chat, Ui.Shell.CurrentPage?.Key);
        Assert.Equal(4, session.Inspector.Steps.Count);
        Assert.All(session.Inspector.Steps, step => Assert.Equal(AgentPlanStepState.Done, step.State));
        Assert.Equal(3, session.Inspector.Sources.Count);
        // The conversation is in the list the sidebar shows, titled by the first words.
        Assert.Contains(Ui.Shell.Hub.Conversations, c => c.Title.StartsWith("帮我梳理"));
        Assert.NotNull(Ui.Pages(window).View);
    }

    [AvaloniaFact]
    public void The_thinking_state_is_held_for_a_screenshot()
    {
        Ui.Open("zh-CN");
        var session = Ui.Service<ChatSession>();
        SampleChatService.HoldAt = "thinking";
        Ui.Shell.Hub.Start(Question);
        Ui.Settle(60);

        // Still waiting: no plan yet, nothing finished.
        Assert.Equal(GenerationStatus.Waiting, session.Inspector.Status);
        Assert.Empty(session.Inspector.Steps);
    }

    [AvaloniaFact]
    public void The_tools_state_shows_the_plan_with_a_step_running()
    {
        Ui.Open("zh-CN");
        var session = Ui.Service<ChatSession>();
        SampleChatService.HoldAt = "tools";
        Ui.Shell.Hub.Start(Question);
        Ui.Settle(80);

        Assert.Equal(4, session.Inspector.Steps.Count);
        Assert.Contains(session.Inspector.Steps, step => step.State == AgentPlanStepState.Active);
        Assert.Contains(session.Inspector.Steps, step => step.State == AgentPlanStepState.Done);
    }

    [AvaloniaFact]
    public void The_models_the_composer_offers_are_the_chat_models_and_one_is_the_default()
    {
        Ui.Open();
        var chat = Ui.Service<ChatViewModel>();
        Ui.Go(AppPages.Chat);

        Assert.NotEmpty(chat.Models);
        Assert.DoesNotContain(chat.Models, m => m.Id == "nebula-embed-v2");
        Assert.Contains(chat.Models, m => m.Id == chat.DefaultModelId);
    }

    [AvaloniaFact]
    public void The_stream_events_are_the_contract_s_event_names()
    {
        // The desktop app and the API share them: a name that changes breaks the other side.
        Assert.Equal("message.start", StreamEvents.MessageStart);
        Assert.Equal("text.delta", StreamEvents.TextDelta);
        Assert.Equal("message.done", StreamEvents.MessageDone);
    }

    [AvaloniaTheory]
    [InlineData(ArcMotionLevel.Full, true)]
    [InlineData(ArcMotionLevel.Reduced, true)]
    [InlineData(ArcMotionLevel.Off, false)]
    public void The_composer_glows_while_the_answer_is_made_unless_motion_is_off(ArcMotionLevel level, bool glows)
    {
        var window = Ui.Open("zh-CN");
        try
        {
            ArcTheme.Apply(t => t.MotionLevel(level));
            SampleChatService.HoldAt = "thinking";
            Ui.Shell.Hub.Start(Question);
            Ui.Settle(60);
            var chat = window.GetVisualDescendants().OfType<AiChat>().Single();

            Assert.True(chat.IsBusy);
            Assert.Equal(glows, ComposerGlow.IsGlowing(chat));
        }
        finally
        {
            SampleChatService.HoldAt = null;
            ArcTheme.Apply(t => t.MotionLevel(ArcMotionLevel.Off));
        }
    }

    [AvaloniaFact]
    public void The_glow_goes_out_when_the_answer_is_done()
    {
        var window = Ui.Open("zh-CN");
        try
        {
            ArcTheme.Apply(t => t.MotionLevel(ArcMotionLevel.Full));
            var session = Ui.Service<ChatSession>();
            Ui.Shell.Hub.Start(Question);
            Assert.True(Ui.Until(() => session.Inspector.Status == GenerationStatus.Done, 200), "the answer finished");
            Ui.Settle(20);
            var chat = window.GetVisualDescendants().OfType<AiChat>().Single();

            Assert.False(chat.IsBusy);
            Assert.False(ComposerGlow.IsGlowing(chat));
        }
        finally
        {
            ArcTheme.Apply(t => t.MotionLevel(ArcMotionLevel.Off));
        }
    }
}
