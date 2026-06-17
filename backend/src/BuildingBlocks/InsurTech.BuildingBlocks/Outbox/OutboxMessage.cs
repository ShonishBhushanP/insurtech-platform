namespace InsurTech.BuildingBlocks.Outbox;

/// <summary>
/// Transactional outbox row (LLD Appendix A.1.5 / A.1.8). Written in the same DB transaction
/// as the aggregate change; a background dispatcher tails undispatched rows and publishes
/// them to the bus, then stamps <see cref="DispatchedUtc"/>.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DispatchedUtc { get; set; }
    public int DispatchAttempts { get; set; }
}
