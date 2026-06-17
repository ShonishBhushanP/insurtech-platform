using InsurTech.Claims.Domain;
using InsurTech.Claims.Domain.Aggregates;

namespace InsurTech.Claims.Application.Abstractions;

/// <summary>Persistence boundary for the Claim aggregate (implemented over EF Core in Infrastructure).</summary>
public interface IClaimRepository
{
    void Add(Claim claim);
    Task<Claim?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Claim>> ListAsync(Guid? policyId, string? filedByUserId, ClaimStatus? status, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

// ---- Cross-service client abstractions (typed HTTP clients in Azure; see LLD A.1.4 "Outbound") ----

public record FraudScoreRequest(Guid ClaimId, Guid PolicyId, string CustomerId, ClaimType ClaimType, decimal ClaimedAmount, string Summary);
public record ShapContribution(string Feature, double Value);
public record FraudScoreResult(double Score, string Decision, IReadOnlyList<ShapContribution> ShapTopN, string ModelVersion);

/// <summary>Calls Fraud Detection /v1/fraud/score. Fail-open per LLD A.2.6 (FR-050).</summary>
public interface IFraudScoringClient
{
    Task<FraudScoreResult> ScoreAsync(FraudScoreRequest request, CancellationToken ct = default);
}

public record PaymentRequest(Guid ClaimId, decimal Amount, string Currency, string IdempotencyKey);
public record PaymentResult(bool Captured, string Reference, string? FailureReason);

/// <summary>Calls Payments & Settlement /v1/payments (LLD A.6).</summary>
public interface IPaymentClient
{
    Task<PaymentResult> CaptureAsync(PaymentRequest request, CancellationToken ct = default);
}
