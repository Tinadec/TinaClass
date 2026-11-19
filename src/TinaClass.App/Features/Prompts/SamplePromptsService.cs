using System.Text.Json;
using System.Text.RegularExpressions;
using TinaClass.App.Common.Sample;
using TinaClass.Contracts.Prompts;
using TinaClass.Contracts.Sample;

namespace TinaClass.App.Features.Prompts;

/// <summary>
/// The seven sample prompts, in memory. <c>/review</c> is at v7 with three versions and three test cases that all pass. A trial run answers with a
/// built-in text (the "TinaClass 内置示例" provider's way: no model, the same words every time), which mentions the points the sample cases expect.
/// </summary>
public sealed partial class SamplePromptsService : IPromptsService
{
    private readonly List<PromptDto> _prompts = [.. PromptsSample.Prompts.Select(p => new PromptDto(p.Id, p.Name, p.Command, "workspace", p.Version, p.Uses, SampleCore.NameOf(p.OwnerId), Variables(p.System + p.User),
        p.System, p.User, p.Id == SampleIds.Prompt(1) ? PromptsSample.ReviewCases.Count : 0, p.Id == SampleIds.Prompt(1) ? PromptsSample.ReviewCases.Count : 0, SampleClock.At(p.Version % 5, 10, 30), 1))];

    private readonly Dictionary<Guid, List<PromptVersionDto>> _versions = [];

    [GeneratedRegex(@"\{\{(.+?)\}\}")]
    private static partial Regex VariablePattern();

    public static IReadOnlyList<string> Variables(string text) => [.. VariablePattern().Matches(text).Select(m => m.Groups[1].Value.Trim()).Distinct()];

    public Task<IReadOnlyList<PromptDto>> ListAsync(string? query = null, CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<PromptDto>>([.. _prompts.Where(p => string.IsNullOrWhiteSpace(query) || p.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) || p.Command.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))]);

    public Task<PromptDto> CreateAsync(string name, string command, CancellationToken cancellation = default)
    {
        var created = new PromptDto(Guid.NewGuid(), name, command, "private", 0, 0, SampleCore.People[0].Name, [], string.Empty, string.Empty, 0, 0, SampleClock.Now, 1);
        _prompts.Insert(0, created);
        return Task.FromResult(created);
    }

    public Task<PromptDto> SaveAsync(PromptDto prompt, string systemText, string userText, CancellationToken cancellation = default)
    {
        var updated = prompt with { SystemText = systemText, UserText = userText, Variables = Variables(systemText + userText), UpdatedAt = SampleClock.Now, Version = prompt.Version + 1 };
        _prompts[_prompts.FindIndex(p => p.Id == prompt.Id)] = updated;
        return Task.FromResult(updated);
    }

    public Task<PromptDto> PublishAsync(Guid id, string note, CancellationToken cancellation = default)
    {
        var index = _prompts.FindIndex(p => p.Id == id);
        var prompt = _prompts[index];
        var versions = VersionList(prompt);
        var next = prompt.CurrentVersion + 1;
        versions.Insert(0, new PromptVersionDto(Guid.NewGuid(), next, note, prompt.SystemText, prompt.UserText, true, SampleCore.People[0].Name, SampleClock.Now));
        _prompts[index] = prompt with { CurrentVersion = next, UpdatedAt = SampleClock.Now };
        return Task.FromResult(_prompts[index]);
    }

    public Task<IReadOnlyList<PromptVersionDto>> VersionsAsync(Guid id, CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<PromptVersionDto>>([.. VersionList(_prompts.First(p => p.Id == id))]);

    private List<PromptVersionDto> VersionList(PromptDto prompt)
    {
        if (_versions.TryGetValue(prompt.Id, out var existing))
        {
            return existing;
        }
        var list = new List<PromptVersionDto>();
        if (prompt.Id == SampleIds.Prompt(1))
        {
            // v7 is the current text; the older ones differ in the wording, so the compare view has something to show.
            var older = new[] { prompt.SystemText.Replace("每项限制为三条意见", "每项最多三条意见"), prompt.SystemText.Replace("，每项限制为三条意见", "") };
            list.Add(new PromptVersionDto(Guid.NewGuid(), 7, PromptsSample.ReviewVersions[0].Note, prompt.SystemText, prompt.UserText, true, SampleCore.NameOf(PromptsSample.ReviewVersions[0].By), SampleClock.At(PromptsSample.ReviewVersions[0].DaysAgo, 15, 20)));
            for (var i = 1; i < PromptsSample.ReviewVersions.Count; i++)
            {
                var v = PromptsSample.ReviewVersions[i];
                list.Add(new PromptVersionDto(Guid.NewGuid(), v.Number, v.Note, older[i - 1], prompt.UserText.Replace("（读者：{{读者角色}}）", i == 2 ? string.Empty : "（读者：{{读者角色}}）"), true, SampleCore.NameOf(v.By), SampleClock.At(v.DaysAgo, 15, 20)));
            }
        }
        else if (prompt.CurrentVersion > 0)
        {
            list.Add(new PromptVersionDto(Guid.NewGuid(), prompt.CurrentVersion, string.Empty, prompt.SystemText, prompt.UserText, true, prompt.OwnerName, prompt.UpdatedAt));
        }
        _versions[prompt.Id] = list;
        return list;
    }

    public Task<IReadOnlyList<PromptCaseDto>> CasesAsync(Guid id, CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<PromptCaseDto>>(id == SampleIds.Prompt(1)
            ? [.. PromptsSample.ReviewCases.Select((c, i) => new PromptCaseDto(SampleIds.Message(9000 + i), c.Name, c.Input, c.Expect, true))]
            : []);

    public Task<RunPromptResponse> RunAsync(Guid id, IReadOnlyDictionary<string, string> variables, string? modelCode, CancellationToken cancellation = default)
    {
        var prompt = _prompts.First(p => p.Id == id);
        var output = Answer(prompt, variables);
        return Task.FromResult(new RunPromptResponse(output, (prompt.SystemText.Length + prompt.UserText.Length) / 2, output.Length / 2, 0));
    }

    public async Task<CasesRunDto> RunCasesAsync(Guid id, CancellationToken cancellation = default)
    {
        var prompt = _prompts.First(p => p.Id == id);
        var cases = (await CasesAsync(id, cancellation)).Select(c =>
        {
            var values = ParseValues(c.Input);
            return c with { Passed = Covers(Answer(prompt, values), c.Expect) };
        }).ToList();
        _prompts[_prompts.FindIndex(p => p.Id == id)] = prompt with { CasesPassed = cases.Count(c => c.Passed == true), CasesTotal = cases.Count };
        return new CasesRunDto(cases.Count(c => c.Passed == true), cases.Count, cases);
    }

    /// <summary>The sample cases expect Chinese words; the built-in answer is in the language of the app, so the same point counts in the other languages too.</summary>
    private static bool Covers(string answer, string expect) =>
        answer.Contains(expect, StringComparison.Ordinal) || expect switch
        {
            "权限" => answer.Contains("Permissions", StringComparison.Ordinal) || answer.Contains("權限", StringComparison.Ordinal),
            "通知" => answer.Contains("Notifications", StringComparison.Ordinal),
            "保留" => answer.Contains("Retention", StringComparison.Ordinal),
            _ => false,
        };

    /// <summary>The variable values of a test case (a JSON object of strings); parsed without reflection.</summary>
    public static Dictionary<string, string> ParseValues(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString())
            : [];
    }

    /// <summary>The built-in answer: for the review prompt a three-point review that covers the points the sample cases expect, for the others an echo of the filled-in message.</summary>
    private static string Answer(PromptDto prompt, IReadOnlyDictionary<string, string> variables)
    {
        if (prompt.Id == SampleIds.Prompt(1))
        {
            return SampleClock.Pick(
                "1. Permissions: say who may export in bulk and for how long records are kept.\n2. Notifications: confirm the email and in-app message on completion.\n3. Retention: state the period and what happens when it expires.",
                "1. 权限：写明谁可以批量导出，以及导出记录保留多久。\n2. 通知：确认完成后的站内信和邮件通知。\n3. 保留：说明保留期限，以及到期后如何处理。",
                "1. 權限：寫明誰可以批次匯出，以及匯出記錄保留多久。\n2. 通知：確認完成後的站內信和郵件通知。\n3. 保留：說明保留期限，以及到期後如何處理。");
        }
        var filled = prompt.UserText;
        foreach (var (key, value) in variables)
        {
            filled = filled.Replace("{{" + key + "}}", value);
        }
        return SampleClock.Pick("Echo of the filled-in message:\n", "已填入变量的消息：\n", "已填入變數的訊息：\n") + filled;
    }
}
