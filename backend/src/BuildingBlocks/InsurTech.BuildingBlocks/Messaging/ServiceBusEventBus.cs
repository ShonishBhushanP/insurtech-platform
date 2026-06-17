using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace InsurTech.BuildingBlocks.Messaging;

/// <summary>
/// Azure Service Bus implementation of <see cref="IEventBus"/> (LLD TR-03 / deployment diagram
/// "Shared Platform — Service Bus"). Publishes each <see cref="IntegrationEvent"/> to a topic
/// named by <see cref="IntegrationEvent.Topic"/>, with the aggregate/session id as the
/// SessionId for per-entity ordering. Activated when a Service Bus namespace is configured.
/// </summary>
public sealed class ServiceBusEventBus(ServiceBusClient client, ILogger<ServiceBusEventBus> logger)
    : IEventBus, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public async Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default)
    {
        var sender = _senders.GetOrAdd(@event.Topic, client.CreateSender);
        var message = new ServiceBusMessage(JsonSerializer.Serialize(@event, @event.GetType()))
        {
            Subject = @event.GetType().Name,
            MessageId = @event.EventId.ToString(),
            ContentType = "application/json"
        };
        await sender.SendMessageAsync(message, ct);
        logger.LogInformation("Published {EventType} to Service Bus topic {Topic}", @event.GetType().Name, @event.Topic);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values) await sender.DisposeAsync();
    }
}
