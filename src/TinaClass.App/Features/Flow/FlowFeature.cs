using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Flow;

/// <summary>Workflows (<c>features.workflows</c>, off by default; needs agents): the canvas, the draft and its publishing, a run and its trace.</summary>
public static class FlowFeature
{
    public static IServiceCollection AddFlow(this IServiceCollection services)
    {
        services.AddSingleton<IFlowService, SampleFlowService>();
        services.AddSingleton<FlowViewModel>();
        return services;
    }
}
