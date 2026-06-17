namespace InsurTech.Fraud.Api.Domain;

public enum FraudCaseStatus { Open, UnderReview, ConfirmedFraud, ConfirmedLegit, Appealed }

/// <summary>
/// FraudCase aggregate (LLD A.2.5). Opened whenever a score yields decision != allow.
/// Drives the Adjuster "Fraud & Risk Alerts" work queue.
/// </summary>
public class FraudCase
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ClaimId { get; private set; }
    public Guid PolicyId { get; private set; }
    public FraudCaseStatus Status { get; private set; }
    public double InitialScore { get; private set; }
    public string Severity { get; private set; } = "Low";
    public bool DuplicateSuspected { get; private set; }
    public string ModelVersion { get; private set; } = default!;
    public string ShapJson { get; private set; } = "[]";
    public string ClaimSummary { get; private set; } = default!;
    public string? DecisionReason { get; private set; }
    public string? InvestigatorUserId { get; private set; }
    public DateTimeOffset OpenedUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedUtc { get; private set; }

    private FraudCase() { }

    public static FraudCase Open(Guid claimId, Guid policyId, double score, string decision,
        bool duplicate, string modelVersion, string shapJson, string summary)
    {
        return new FraudCase
        {
            ClaimId = claimId,
            PolicyId = policyId,
            InitialScore = score,
            Status = decision.Equals("block", StringComparison.OrdinalIgnoreCase)
                ? FraudCaseStatus.UnderReview : FraudCaseStatus.Open,
            Severity = score >= 0.85 ? "High" : score >= 0.55 ? "Medium" : "Low",
            DuplicateSuspected = duplicate,
            ModelVersion = modelVersion,
            ShapJson = shapJson,
            ClaimSummary = summary
        };
    }

    public void Decide(FraudCaseStatus outcome, string reason, string investigatorUserId)
    {
        if (Status is FraudCaseStatus.ConfirmedFraud or FraudCaseStatus.ConfirmedLegit)
            throw new InvalidOperationException("FR-020: case already closed.");
        if (outcome is not (FraudCaseStatus.ConfirmedFraud or FraudCaseStatus.ConfirmedLegit))
            throw new InvalidOperationException("FR-020: decision must be ConfirmedFraud or ConfirmedLegit.");
        Status = outcome;
        DecisionReason = reason;
        InvestigatorUserId = investigatorUserId;
        ClosedUtc = DateTimeOffset.UtcNow;
    }
}
