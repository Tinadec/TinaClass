using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Knowledge;
using TinaClass.Contracts.Knowledge;
using Xunit;

namespace TinaClass.App.Tests.Features.Knowledge;

public class KnowledgeTests
{
    private static KnowledgeViewModel Open(string language = "en-US")
    {
        Ui.Open(language);
        Ui.Go(AppPages.Knowledge);
        var knowledge = Ui.Service<KnowledgeViewModel>();
        Assert.True(Ui.Until(() => knowledge.Documents.Count > 0));
        return knowledge;
    }

    [AvaloniaFact]
    public void The_page_lists_the_four_bases_and_opens_the_first_with_its_named_documents()
    {
        var knowledge = Open();

        Assert.Equal(4, knowledge.Bases.Count);
        Assert.Equal(knowledge.Bases[0], knowledge.SelectedBase);
        Assert.Equal(KnowledgeSample.Documents.Count, knowledge.Documents.Count);
        Assert.Equal(knowledge.SelectedBase!.Name, Assert.Single(knowledge.Crumbs));
        // Five ready, two in progress, one queued, one failed (design 16).
        Assert.Equal(5, knowledge.Documents.Count(d => d.Dto.State == DocumentStates.Ready));
        Assert.Equal(3, knowledge.Documents.Count(d => d.InProgress));
        Assert.Equal(1, knowledge.Documents.Count(d => d.IsFailed));
        Assert.False(knowledge.HasNoBases);
        Assert.False(knowledge.HasNoDocuments);
    }

    [AvaloniaFact]
    public void A_base_without_named_documents_still_shows_rows_and_the_counts_follow_the_base()
    {
        var knowledge = Open();

        knowledge.SelectedBase = knowledge.Bases[2];
        Assert.True(Ui.Until(() => knowledge.Documents.Count > 0 && knowledge.Documents.All(d => d.Dto.BaseId == knowledge.Bases[2].Id)));
        Assert.Contains("312", knowledge.Counts);
    }

    [AvaloniaFact]
    public void The_filter_narrows_the_documents_by_name()
    {
        var knowledge = Open();

        knowledge.Filter = "xlsx";
        Assert.True(Ui.Until(() => knowledge.Documents.Count == 3));
        Assert.All(knowledge.Documents, d => Assert.Contains("xlsx", d.Name));

        knowledge.Filter = "no such file";
        Assert.True(Ui.Until(() => knowledge.Documents.Count == 0));
        Assert.True(knowledge.HasNoDocuments);
    }

    [AvaloniaFact]
    public async Task A_new_document_is_queued_and_a_deleted_one_goes()
    {
        var knowledge = Open();
        var before = knowledge.Documents.Count;

        var row = await knowledge.AddAsync("notes.md", "text/markdown", 2_048, "some text");
        Assert.NotNull(row);
        Assert.Equal(before + 1, knowledge.Documents.Count);
        Assert.Same(row, knowledge.Documents[0]);
        Assert.True(row!.InProgress);
        Assert.Equal("2 KB", row.Size);

        await knowledge.DeleteAsync(row);
        Assert.Equal(before, knowledge.Documents.Count);
        Assert.DoesNotContain(knowledge.Documents, d => d.Id == row.Id);
    }

    [AvaloniaFact]
    public void Retrying_the_failed_document_queues_it_again_and_clears_the_error()
    {
        var knowledge = Open();
        var failed = knowledge.Documents.Single(d => d.IsFailed);
        Assert.False(string.IsNullOrEmpty(failed.Error));

        knowledge.RetryCommand.Execute(failed);
        Assert.True(Ui.Until(() => !failed.IsFailed));
        Assert.True(failed.InProgress);
        Assert.Null(failed.Error);
    }

    [AvaloniaFact]
    public void A_search_returns_scored_hits_with_the_document_and_an_unknown_word_finds_nothing()
    {
        var knowledge = Open("zh-CN");

        knowledge.TestQuery = "导出";
        knowledge.SearchCommand.Execute(null);
        Assert.True(Ui.Until(() => knowledge.HasSearched));
        Assert.NotEmpty(knowledge.Hits);
        Assert.All(knowledge.Hits, h => Assert.InRange(h.ScoreValue, 0.5, 1.0));
        Assert.Equal(knowledge.Hits.OrderByDescending(h => h.ScoreValue).Select(h => h.Text), knowledge.Hits.Select(h => h.Text));
        Assert.False(knowledge.HasNoHits);
        Assert.False(string.IsNullOrEmpty(knowledge.HitsSummary));

        knowledge.TestQuery = "zzzzqqqq";
        knowledge.SearchCommand.Execute(null);
        Assert.True(Ui.Until(() => knowledge.HasNoHits));
        Assert.Empty(knowledge.Hits);
    }

    [AvaloniaFact]
    public async Task Creating_a_base_selects_it()
    {
        var knowledge = Open();

        await knowledge.CreateBaseAsync("Contracts", "Templates and clauses");

        Assert.Equal(5, knowledge.Bases.Count);
        Assert.Equal("Contracts", knowledge.SelectedBase?.Name);
        Assert.Equal("Contracts", knowledge.Title);
        Assert.True(Ui.Until(() => knowledge.HasNoDocuments || knowledge.Documents.Count > 0));
    }

    [AvaloniaFact]
    public void The_words_follow_the_language()
    {
        var knowledge = Open();
        var english = knowledge.Documents.First(d => d.Dto.State == DocumentStates.Ready).StateText;

        Ui.Shell.SetLanguageCommand.Execute("zh-CN");
        Ui.Settle(6);
        var chinese = knowledge.Documents.First(d => d.Dto.State == DocumentStates.Ready).StateText;

        Assert.NotEqual(english, chinese);
        Assert.Matches("[一-鿿]", chinese);
    }
}
