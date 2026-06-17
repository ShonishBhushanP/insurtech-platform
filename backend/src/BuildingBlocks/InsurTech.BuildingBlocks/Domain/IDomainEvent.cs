namespace InsurTech.BuildingBlocks.Domain;

/// <summary>
/// A fact that happened inside an aggregate. Becomes an integration event on the
/// bus once the owning transaction commits (LLD §4.1.1 — Event Grid broadcasts domain events).
/// </summary>
public interface IDomainEvent
{
    Guid EventId => Guid.NewGuid();
    DateTimeOffset OccurredUtc => DateTimeOffset.UtcNow;
    string EventType { get; }
}
