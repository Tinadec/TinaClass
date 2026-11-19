using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Home;
using Xunit;

namespace TinaClass.App.Tests.Features.Home;

public class HomeTests
{
    [AvaloniaFact]
    public void Home_greets_the_person_by_the_hour_in_the_chosen_language()
    {
        Ui.Open("zh-CN");
        Ui.Go(AppPages.Home);
        var home = Ui.Service<HomeViewModel>();
        // The test clock is Friday 10:42.
        Assert.Equal("沈知远，上午好", home.Greeting);

        Ui.Open("en-US");
        Ui.Go(AppPages.Home);
        Assert.Equal("Good morning, 沈知远", Ui.Service<HomeViewModel>().Greeting);
    }

    [AvaloniaFact]
    public void Home_lists_the_four_newest_conversations_and_the_starters()
    {
        Ui.Open();
        Ui.Go(AppPages.Home);
        var home = Ui.Service<HomeViewModel>();

        Assert.Equal(4, home.Recent.Count);
        Assert.Equal(home.Recent.Select(r => r.Id), home.Hub.Conversations.OrderByDescending(c => c.UpdatedAt).Take(4).Select(c => c.Id));
        Assert.NotEmpty(home.Suggestions);
        Assert.NotNull(home.DefaultModel);
    }

    [AvaloniaFact]
    public void Opening_a_recent_conversation_shows_the_chat_on_it()
    {
        Ui.Open();
        Ui.Go(AppPages.Home);
        var home = Ui.Service<HomeViewModel>();
        var target = home.Recent[1];

        home.OpenCommand.Execute(target);
        Ui.Settle(10);

        Assert.Equal(AppPages.Chat, Ui.Shell.CurrentPage?.Key);
        Assert.Equal(target.Id, Ui.Shell.Hub.CurrentId);
    }

    [AvaloniaFact]
    public void Writing_on_home_starts_a_new_chat_with_the_text()
    {
        Ui.Open();
        Ui.Go(AppPages.Home);
        Ui.Shell.Hub.Start("a first message");

        Assert.True(Ui.Until(() => Ui.Shell.Hub.Conversations.Any(c => c.Title == "a first message"), 200));
        Assert.Equal(AppPages.Chat, Ui.Shell.CurrentPage?.Key);
    }
}
