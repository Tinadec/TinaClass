using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Models;

/// <summary>Models (a core feature, no switch): the provider and model lists, keys and connection tests.</summary>
public static class ModelsFeature
{
    public static IServiceCollection AddModels(this IServiceCollection services)
    {
        services.AddSingleton<IModelsService, SampleModelsService>();
        services.AddSingleton<ModelsViewModel>();
        return services;
    }
}
