using TinaClass.App.Common.Api;
using TinaClass.App.Features.Prompts.Localization;
using TinaClass.Contracts.Common;
using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Prompts;

/// <summary>Prompts (<c>features.prompts</c>): the prompt list and Arc's <c>PromptStudio</c> (texts, variables, versions, test cases, trial runs).</summary>
public static class PromptsFeature
{
    public static IServiceCollection AddPrompts(this IServiceCollection services)
    {
        services.AddSingleton<IPromptsService, SamplePromptsService>();
        services.AddSingleton<PromptsViewModel>();
        // The text of the error the server answers with (409) when this feature's rule is broken.
        ErrorText.Register(ErrorCodes.CommandTaken, () => PromptsLangResources.Get(PromptsLangResourceKind.ErrorCommandTaken));
        return services;
    }
}
