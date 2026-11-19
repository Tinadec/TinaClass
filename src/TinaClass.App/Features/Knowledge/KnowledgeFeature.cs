using TinaClass.App.Common.Api;
using TinaClass.App.Features.Knowledge.Localization;
using TinaClass.Contracts.Common;
using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Knowledge;

/// <summary>Knowledge (<c>features.knowledge</c>): bases, documents with their indexing, and the retrieval test.</summary>
public static class KnowledgeFeature
{
    public static IServiceCollection AddKnowledge(this IServiceCollection services)
    {
        services.AddSingleton<IKnowledgeService, SampleKnowledgeService>();
        services.AddSingleton<KnowledgeViewModel>();
        // The text of the error the server answers with (409) when this feature's rule is broken.
        ErrorText.Register(ErrorCodes.DuplicateDocument, () => KnowledgeLangResources.Get(KnowledgeLangResourceKind.ErrorDuplicate));
        return services;
    }
}
