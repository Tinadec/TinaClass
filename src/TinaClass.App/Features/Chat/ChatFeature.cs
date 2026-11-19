using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Chat;

public static class ChatFeature
{
    /// <summary>Chat is core: the service (sample data until the API replaces it), the shared conversation list and inspector, the responder and the page.</summary>
    public static IServiceCollection AddChat(this IServiceCollection services)
    {
        services.AddSingleton<IChatService, SampleChatService>();
        services.AddSingleton<ChatHub>();
        services.AddSingleton<ChatSession>();
        services.AddSingleton<ChatResponder>();
        services.AddSingleton<ChatViewModel>();
        return services;
    }
}
