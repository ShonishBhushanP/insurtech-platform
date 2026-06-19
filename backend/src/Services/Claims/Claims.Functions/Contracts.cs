namespace InsurTech.Claims.Functions;

// Orchestration input (matches Claims.Application.Adjudication.AdjudicationInput by JSON shape).
public record AdjudicationInput(
    Guid ClaimId,
    Guid PolicyId,
    string CustomerId,
    string ClaimType,
    decimal ClaimedAmount,
    string Currency,
    string Summary,
    decimal AutoApproveThreshold);

// Activity payloads / results.
public record FraudResult(double Score, string Decision);
public record TriageInput(Guid ClaimId, double Score, string Decision);
public record ReferInput(Guid ClaimId, string Reason);
public record ApproveInput(Guid ClaimId, decimal Amount, string Currency);
public record PaymentResult(bool Captured, string Reference);
public record MarkPaidInput(Guid ClaimId, string Reference);
