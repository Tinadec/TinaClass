using TinaClass.App.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinaClass.App.Features.Usage;

/// <summary>Usage (<c>features.usage</c>): the usage page and the sidebar's spend footer.</summary>
public static class UsageFeature
{
    public static IServiceCollection AddUsage(this IServiceCollection services)
    {
        services.AddSingleton<IUsageService, SampleUsageService>();
        services.Replace(ServiceDescriptor.Singleton<ISpendInfo, UsageSpendInfo>());
        services.AddSingleton<UsageViewModel>();
        return services;
    }
}
