using InsurTech.Claims.Application.Abstractions;
using InsurTech.Claims.Domain;
using InsurTech.Claims.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace InsurTech.Claims.Application.Adjudication;

public sealed class AdjudicationOptions
{
    /// <summary>Claims at or below this amount with a clean fraud signal auto-approve (LLD A.1.3.2 decide step).</summary>
    public decimal AutoApproveThreshold { get; set; } = 100_000m;
}

/// <summary>
/// In-process stand-in for the Durable Functions adjudication saga (LLD A.1.3.2).
/// Sequence: fraud score → triage → decide (rules) → settle (Payments) → notify.
/// In Azure this is an orchestrator with fan-out activities; here it is a linear async
/// service invoked off the ClaimFiled event so the synchronous filing path stays fast.
/// </summary>
public sealed class AdjudicationService(
    IClaimRepository repository,
    IFraudScoringClient fraud,
    IPaymentClient payments,
    AdjudicationOptions options,
    ILogger<AdjudicationService> logger)
{
    public async Task RunAsync(Guid claimId, CancellationToken ct = default)
    {
        var claim = await repository.GetAsync(claimId, ct);
        if (claim is null) { logger.LogWarning("Adjudication: claim {ClaimId} not found", claimId); return; }
        if (claim.Status != ClaimStatus.Filed) { logger.LogInformation("Adjudication: claim {ClaimId} already {Status}", claimId, claim.Status); return; }

        // 1) Fraud scoring (fail-open: a Fraud outage yields decision=refer, never blocks the saga).
        var score = await fraud.ScoreAsync(new FraudScoreRequest(
            claim.Id, claim.PolicyId, claim.FiledByUserId, claim.Type, claim.ClaimedAmount, claim.IncidentDescription), ct);

        // 2) Triage
        claim.ApplyTriage(score.Score, score.Decision);
        await repository.SaveChangesAsync(ct);

        // 3) Decide
        if (claim.Status == ClaimStatus.UnderInvestigation)
        {
            logger.LogInformation("Claim {ClaimId} diverted to SIU investigation (score {Score})", claimId, score.Score);
            return; // awaits human investigator decision
        }

        if (score.Decision.Equals("refer", StringComparison.OrdinalIgnoreCase))
        {
            claim.ReferForUnderwriting($"Fraud risk score {score.Score:0.00} in refer band.");
            await repository.SaveChangesAsync(ct);
            return; // awaits underwriter decision (human-in-loop, 72h timer in Azure)
        }

        if (claim.ClaimedAmount > options.AutoApproveThreshold)
        {
            claim.ReferForUnderwriting($"Claimed amount {claim.ClaimedAmount:N0} exceeds auto-approve threshold.");
            await repository.SaveChangesAsync(ct);
            return;
        }

        // 4) Auto-approve + settle (happy path)
        claim.Approve(Money.Of(claim.ClaimedAmount, claim.CurrencyCode));
        await repository.SaveChangesAsync(ct);

        var payment = await payments.CaptureAsync(new PaymentRequest(
            claim.Id, claim.ApprovedAmount ?? claim.ClaimedAmount, claim.CurrencyCode, claim.Id.ToString("N")), ct);

        if (payment.Captured)
        {
            claim.MarkPaid(payment.Reference);
            await repository.SaveChangesAsync(ct);
            logger.LogInformation("Claim {ClaimId} settled, payment ref {Ref}", claimId, payment.Reference);
        }
        else
        {
            // Compensation surface (LLD A.1.3.3): leave Approved, alert; retry/refund owned by saga.
            logger.LogWarning("Claim {ClaimId} payment declined: {Reason}", claimId, payment.FailureReason);
        }
    }
}
