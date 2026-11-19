using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Prompts;
using Xunit;

namespace TinaClass.App.Tests.Features.Prompts;

public class PromptsTests
{
    private static PromptsViewModel Open(string language = "en-US")
    {
        Ui.Open(language);
        Ui.Go(AppPages.Prompts);
        var prompts = Ui.Service<PromptsViewModel>();
        Assert.True(Ui.Until(() => prompts.Selected is not null && prompts.Versions.Count > 0));
        return prompts;
    }

    [AvaloniaFact]
    public void The_page_lists_the_seven_prompts_and_opens_the_review_prompt_at_v7()
    {
        var prompts = Open();

        Assert.Equal(7, prompts.Items.Count);
        Assert.Equal("/review", prompts.Selected!.Command);
        Assert.Equal(7, prompts.Selected.Dto.CurrentVersion);
        Assert.Equal(3, prompts.Versions.Count);
        Assert.Equal(3, prompts.Cases.Count);
        Assert.Equal(["文档", "读者角色"], prompts.Selected.Dto.Variables.Order(StringComparer.Ordinal));
        Assert.True(prompts.Selected.HasCases);
        Assert.True(prompts.Selected.CasesAllPass);
    }

    [AvaloniaFact]
    public void The_search_narrows_the_list_by_name_or_command()
    {
        var prompts = Open();

        prompts.Query = "/sql";
        Assert.True(Ui.Until(() => prompts.Items.Count == 1));
        Assert.Equal("SQL 解释器", prompts.Items[0].Name);

        prompts.Query = "nothing like this";
        Assert.True(Ui.Until(() => prompts.Items.Count == 0));
        Assert.True(prompts.HasNoPrompts);
    }

    [AvaloniaFact]
    public void Editing_marks_the_draft_and_saving_clears_it()
    {
        var prompts = Open();
        Assert.Contains("v7", prompts.BarNote);

        prompts.SetDraft("a new system text with {{x}}", prompts.Selected!.Dto.UserText);
        Assert.Equal("Unsaved changes", prompts.BarNote);

        prompts.SaveCommand.Execute(null);
        Assert.True(Ui.Until(() => prompts.BarNote != "Unsaved changes"));
        Assert.Contains("x", prompts.Selected.Dto.Variables);
    }

    [AvaloniaFact]
    public async Task Publishing_adds_a_version_with_the_note()
    {
        var prompts = Open();

        await prompts.PublishAsync("Tighter wording");

        Assert.Equal(8, prompts.Selected!.Dto.CurrentVersion);
        Assert.Equal(4, prompts.Versions.Count);
        Assert.Equal("Tighter wording", prompts.Versions[0].Note);
        Assert.Equal(8, prompts.Versions[0].Number);
    }

    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void The_three_test_cases_pass_in_every_language(string language)
    {
        var prompts = Open(language);

        prompts.RunCasesCommand.Execute(null);

        Assert.True(Ui.Until(() => prompts.CasesResult is not null));
        Assert.True(prompts.CasesPass);
        Assert.Contains("3", prompts.CasesResult);
    }

    [AvaloniaFact]
    public async Task A_new_prompt_is_private_and_selected()
    {
        var prompts = Open();

        await prompts.CreateAsync("Release notes", "/notes");

        Assert.Equal(8, prompts.Items.Count);
        Assert.Equal("/notes", prompts.Selected?.Command);
        Assert.Equal(0, prompts.Selected!.Dto.CurrentVersion);
    }

    [AvaloniaFact]
    public async Task A_trial_run_fills_in_the_variables_with_the_built_in_answer()
    {
        var prompts = Open();
        prompts.Selected = prompts.Items.First(p => p.Command == "/sql");

        var answer = await prompts.Service.RunAsync(prompts.Selected.Id, new Dictionary<string, string> { ["sql"] = "select 1" }, null);

        Assert.Contains("select 1", answer.Output);
    }

    [AvaloniaFact]
    public void The_words_follow_the_language()
    {
        var prompts = Open();
        var english = prompts.Selected!.Meta;

        Ui.Shell.SetLanguageCommand.Execute("zh-CN");
        Ui.Settle(6);

        Assert.NotEqual(english, prompts.Selected.Meta);
    }
}
