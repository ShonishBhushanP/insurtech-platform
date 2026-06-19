namespace InsurTech.Claims.Application.Adjudication;

/// <summary>
/// Starts the adjudication saga for a filed claim. Two implementations select by config:
/// <list type="bullet">
/// <item><b>InProcess</b> (local default) — runs <see cref="AdjudicationService"/> in-process.</item>
/// <item><b>DurableFunctions</b> — POSTs to the Claims.Functions HTTP starter so the saga runs
/// as an Azure Durable Functions orchestration (LLD A.1.3.2).</item>
/// </list>
/// </summary>
public interface IAdjudicationTrigger
{
    Task TriggerAsync(Guid claimId, CancellationToken ct = default);
}

/// <summary>Payload handed to the Durable Functions orchestrator (matches Claims.Functions).</summary>
public record AdjudicationInput(
    Guid ClaimId,
    Guid PolicyId,
    string CustomerId,
    string ClaimType,
    decimal ClaimedAmount,
    string Currency,
    string Summary,
    decimal AutoApproveThreshold);

/// <summary>Local default — runs the saga in the Claims process.</summary>
public sealed class InProcessAdjudicationTrigger(AdjudicationService service) : IAdjudicationTrigger
{
    public Task TriggerAsync(Guid claimId, CancellationToken ct = default) => service.RunAsync(claimId, ct);
}
