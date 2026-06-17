namespace InsurTech.BuildingBlocks.Messaging;

/// <summary>
/// Cross-service event carried on the bus. In Azure this rides Service Bus Premium
/// (commands, session = claimId) / Event Grid (broadcast) — LLD §4.1.1, TR-03.
/// Locally an <see cref="IEventBus"/> in-process implementation stands in.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredUtc { get; init; } = DateTimeOffset.UtcNow;
    public abstract string Topic { get; }
}
