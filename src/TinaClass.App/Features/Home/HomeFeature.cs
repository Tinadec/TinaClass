using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Home;

/// <summary>Home (core): greeting, composer, starters and the recent conversations.</summary>
public static class HomeFeature
{
    public static IServiceCollection AddHome(this IServiceCollection services)
    {
        services.AddSingleton<HomeViewModel>();
        return services;
    }
}
