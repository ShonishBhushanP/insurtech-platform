namespace InsurTech.BuildingBlocks.Messaging;

/// <summary>
/// Envelope used by the outbox dispatcher to carry a persisted outbox row onto the bus. The
/// original domain-event JSON travels in <see cref="Payload"/>; <see cref="EventType"/> becomes the
/// message Subject so subscribers can filter without deserializing. All claim events publish to a
/// single topic (configurable; default "claims-events").
/// </summary>
public sealed record OutboxIntegrationEvent(string EventType, Guid AggregateId, string Payload, string TopicName)
    : IntegrationEvent
{
    public override string Topic => TopicName;
}
