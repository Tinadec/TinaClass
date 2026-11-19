using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Settings;

/// <summary>Settings (a core feature, no switch): the assistant's persona, behavior, tool safety and the interface preferences.</summary>
public static class SettingsFeature
{
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SampleSettingsService>();
        services.AddSingleton<SettingsViewModel>();
        return services;
    }
}
