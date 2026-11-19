using TinaClass.Contracts.Prompts;

namespace TinaClass.App.Features.Prompts;

/// <summary>
/// Where the app gets its prompts: the list with the texts, their versions, test cases and trial runs. The sample service keeps them in memory
/// (a trial run answers with a built-in text); <c>Api/ApiPromptsService.cs</c> talks to <c>/prompts</c>, where the run goes through the model provider.
/// </summary>
public interface IPromptsService
{
    Task<IReadOnlyList<PromptDto>> ListAsync(string? query = null, CancellationToken cancellation = default);

    Task<PromptDto> CreateAsync(string name, string command, CancellationToken cancellation = default);

    Task<PromptDto> SaveAsync(PromptDto prompt, string systemText, string userText, CancellationToken cancellation = default);

    /// <summary>Makes the saved texts a new version with a note.</summary>
    Task<PromptDto> PublishAsync(Guid id, string note, CancellationToken cancellation = default);

    Task<IReadOnlyList<PromptVersionDto>> VersionsAsync(Guid id, CancellationToken cancellation = default);

    Task<IReadOnlyList<PromptCaseDto>> CasesAsync(Guid id, CancellationToken cancellation = default);

    /// <summary>Runs the prompt once with the given variable values on a model (by code, or the default).</summary>
    Task<RunPromptResponse> RunAsync(Guid id, IReadOnlyDictionary<string, string> variables, string? modelCode, CancellationToken cancellation = default);

    /// <summary>Runs every test case and says which passed.</summary>
    Task<CasesRunDto> RunCasesAsync(Guid id, CancellationToken cancellation = default);
}
