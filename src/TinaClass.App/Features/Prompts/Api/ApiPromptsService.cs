using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Prompts;

namespace TinaClass.App.Features.Prompts.Api;

/// <summary>The prompts of the API (<c>/prompts</c>): the list, saving, publishing, versions, cases and a trial run through the model provider.</summary>
public sealed class ApiPromptsService(ApiClient api) : IPromptsService
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<IReadOnlyList<PromptDto>> ListAsync(string? query = null, CancellationToken cancellation = default) =>
        api.GetAsync(ApiRoutes.Prompts + (string.IsNullOrWhiteSpace(query) ? string.Empty : "?q=" + Uri.EscapeDataString(query.Trim())), Json.IReadOnlyListPromptDto, cancellation);

    public Task<PromptDto> CreateAsync(string name, string command, CancellationToken cancellation = default) =>
        api.PostAsync(ApiRoutes.Prompts, new SavePromptRequest(name, command, "private", string.Empty, string.Empty, 0), Json.SavePromptRequest, Json.PromptDto, cancellation);

    public Task<PromptDto> SaveAsync(PromptDto prompt, string systemText, string userText, CancellationToken cancellation = default) =>
        api.PatchAsync($"{ApiRoutes.Prompts}/{prompt.Id}", new SavePromptRequest(prompt.Name, prompt.Command, prompt.Visibility, systemText, userText, prompt.Version), Json.SavePromptRequest, Json.PromptDto, cancellation);

    public Task<PromptDto> PublishAsync(Guid id, string note, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Prompts}/{id}/publish", new PublishPromptRequest(note), Json.PublishPromptRequest, Json.PromptDto, cancellation);

    public Task<IReadOnlyList<PromptVersionDto>> VersionsAsync(Guid id, CancellationToken cancellation = default) =>
        api.GetAsync($"{ApiRoutes.Prompts}/{id}/versions", Json.IReadOnlyListPromptVersionDto, cancellation);

    public Task<IReadOnlyList<PromptCaseDto>> CasesAsync(Guid id, CancellationToken cancellation = default) =>
        api.GetAsync($"{ApiRoutes.Prompts}/{id}/cases", Json.IReadOnlyListPromptCaseDto, cancellation);

    public Task<RunPromptResponse> RunAsync(Guid id, IReadOnlyDictionary<string, string> variables, string? modelCode, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Prompts}/{id}/run", new RunPromptRequest(variables, modelCode), Json.RunPromptRequest, Json.RunPromptResponse, cancellation);

    public Task<CasesRunDto> RunCasesAsync(Guid id, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Prompts}/{id}/cases/run", Json.CasesRunDto, cancellation);
}
