namespace TinaClass.Contracts.Prompts;

public sealed record PromptDto(Guid Id, string Name, string Command, string Visibility, int CurrentVersion, int UseCount, string OwnerName, IReadOnlyList<string> Variables,
    string SystemText, string UserText, int CasesPassed, int CasesTotal, DateTimeOffset UpdatedAt, int Version);

public sealed record SavePromptRequest(string Name, string Command, string Visibility, string SystemText, string UserText, int Version);

public sealed record PromptVersionDto(Guid Id, int Number, string Note, string SystemText, string UserText, bool Published, string CreatedByName, DateTimeOffset CreatedAt);

public sealed record PublishPromptRequest(string Note);

public sealed record PromptCaseDto(Guid Id, string Name, string Input, string Expect, bool? Passed);

public sealed record SavePromptCaseRequest(string Name, string Input, string Expect);

public sealed record RunPromptRequest(IReadOnlyDictionary<string, string>? Variables, string? ModelCode);

public sealed record RunPromptResponse(string Output, int TokensIn, int TokensOut, long Cost);

public sealed record CasesRunDto(int Passed, int Total, IReadOnlyList<PromptCaseDto> Cases);

public static class PromptsEvents
{
    public const string Published = "prompt.published";
}
