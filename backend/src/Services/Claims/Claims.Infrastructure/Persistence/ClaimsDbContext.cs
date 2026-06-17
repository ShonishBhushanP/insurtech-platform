using System.Text.Json;
using InsurTech.BuildingBlocks.Domain;
using InsurTech.BuildingBlocks.Outbox;
using InsurTech.Claims.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace InsurTech.Claims.Infrastructure.Persistence;

/// <summary>
/// Claims persistence. On SaveChanges it drains domain events from tracked aggregates into
/// the OutboxEvent table in the same unit of work (transactional outbox — LLD A.1.5 / A.1.8).
/// </summary>
public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<OutboxMessage> OutboxEvents => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // History is persisted as a JSON string on the Claim row (avoids EF InMemory's
        // owned-collection update quirk; in SQL MI this maps to an nvarchar(max) column).
        var historyConverter = new ValueConverter<IReadOnlyList<ClaimStatusEntry>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<ClaimStatusEntry>>(v, (JsonSerializerOptions?)null) ?? new());
        var historyComparer = new ValueComparer<IReadOnlyList<ClaimStatusEntry>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<List<ClaimStatusEntry>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new());

        var docsConverter = new ValueConverter<IReadOnlyList<ClaimDocument>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<ClaimDocument>>(v, (JsonSerializerOptions?)null) ?? new());
        var docsComparer = new ValueComparer<IReadOnlyList<ClaimDocument>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<List<ClaimDocument>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new());

        b.Entity<Claim>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.ClaimNumber).IsRequired();
            e.HasIndex(c => c.ClaimNumber).IsUnique();
            e.HasIndex(c => c.PolicyId);
            e.HasIndex(c => new { c.Status, c.FiledUtc });
            e.Ignore(c => c.DomainEvents);
            e.Property(c => c.History).HasConversion(historyConverter, historyComparer);
            e.Property(c => c.Documents).HasConversion(docsConverter, docsComparer);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasKey(o => o.Id);
            e.HasIndex(o => o.DispatchedUtc);
            e.Property(o => o.EventType).IsRequired();
            e.Property(o => o.Payload).IsRequired();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        DrainDomainEventsToOutbox();
        return await base.SaveChangesAsync(ct);
    }

    private void DrainDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker.Entries<Claim>()
            .Where(x => x.Entity.DomainEvents.Count > 0)
            .Select(x => x.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxEvents.Add(new OutboxMessage
                {
                    AggregateId = aggregate.Id,
                    EventType = domainEvent.EventType,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType())
                });
            }
            aggregate.ClearDomainEvents();
        }
    }
}
