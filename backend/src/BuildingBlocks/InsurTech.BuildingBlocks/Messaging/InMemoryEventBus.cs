using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InsurTech.BuildingBlocks.Messaging;

/// <summary>
/// In-process stand-in for Service Bus / Event Grid used for local runs and tests.
/// Resolves all registered <see cref="IIntegrationEventHandler{TEvent}"/> for the event's
/// runtime type within a fresh DI scope and invokes them. Intra-service only — cross-service
/// calls in the local topology go over typed HTTP clients (mirroring the Durable activities
/// that call Fraud / Payments in LLD A.1.3.2).
/// </summary>
public sealed class InMemoryEventBus(IServiceScopeFactory scopeFactory, ILogger<InMemoryEventBus> logger) : IEventBus
{
    public async Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Publishing {EventType} ({EventId}) on topic {Topic}",
            @event.GetType().Name, @event.EventId, @event.Topic);

        using var scope = scopeFactory.CreateScope();
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(@event.GetType());
        var handlers = scope.ServiceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is null) continue;
            var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;
            await (Task)method.Invoke(handler, new object[] { @event, ct })!;
        }
    }
}
