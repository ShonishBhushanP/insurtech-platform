using InsurTech.Claims.Application.Adjudication;
using InsurTech.Claims.Domain.Events;
using InsurTech.Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InsurTech.Claims.Infrastructure.Outbox;

/// <summary>
/// Background outbox dispatcher (LLD A.1.8). Tails undispatched OutboxEvent rows, "publishes"
/// them (logged here; Service Bus in Azure), and stamps DispatchedUtc. A <see cref="ClaimFiled"/>
/// event additionally kicks off the adjudication saga off the synchronous filing path.
/// Poll cadence 500ms; batch 50 — matching the LLD dispatcher budget.
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchBatchAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Outbox dispatch loop error"); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        var claimsToAdjudicate = new List<Guid>();

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            var batch = await db.OutboxEvents
                .Where(o => o.DispatchedUtc == null)
                .OrderBy(o => o.CreatedUtc)
                .Take(50)
                .ToListAsync(ct);

            if (batch.Count == 0) return;

            foreach (var msg in batch)
            {
                logger.LogInformation("Outbox → bus: {EventType} (aggregate {AggregateId})", msg.EventType, msg.AggregateId);
                msg.DispatchedUtc = DateTimeOffset.UtcNow;
                if (msg.EventType == nameof(ClaimFiled)) claimsToAdjudicate.Add(msg.AggregateId);
            }
            await db.SaveChangesAsync(ct);
        }

        // Run the saga in its own scope per claim (its own DbContext/unit of work).
        foreach (var claimId in claimsToAdjudicate)
        {
            using var scope = scopeFactory.CreateScope();
            var adjudication = scope.ServiceProvider.GetRequiredService<AdjudicationService>();
            await adjudication.RunAsync(claimId, ct);
        }
    }
}
