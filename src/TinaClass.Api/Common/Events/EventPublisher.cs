using System.Text.Json.Serialization.Metadata;

namespace TinaClass.Api.Common.Events;

/// <summary>
/// Raises an event other systems can subscribe to (a feature's <c>…Events</c> constants in Contracts). A feature that delivers
/// them (webhooks, a message queue) replaces the default with <c>services.Replace</c>; without one they are dropped
/// (<see cref="NullEventPublisher"/>).
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<T>(string eventType, T data, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken);
}

internal sealed class NullEventPublisher : IEventPublisher
{
    public Task PublishAsync<T>(string eventType, T data, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) => Task.CompletedTask;
}
