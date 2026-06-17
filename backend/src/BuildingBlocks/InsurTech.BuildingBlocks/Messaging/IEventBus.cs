namespace InsurTech.BuildingBlocks.Messaging;

/// <summary>Publish side of the bus. Maps to Service Bus / Event Grid in Azure.</summary>
public interface IEventBus
{
    Task PublishAsync(IntegrationEvent @event, CancellationToken ct = default);
}

/// <summary>A handler for a specific integration event topic.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
