namespace InsurTech.BuildingBlocks.Domain;

/// <summary>
/// Base class for domain aggregate roots. Collects domain events that the
/// infrastructure layer drains into the outbox in the same transaction
/// (transactional outbox pattern — LLD Appendix A.1.8 / TR-07).
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>Domain events raised but not yet dispatched.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Marker for aggregate roots — the only entities a repository persists directly.</summary>
public interface IAggregateRoot;
