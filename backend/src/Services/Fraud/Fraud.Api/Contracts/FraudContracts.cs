using InsurTech.Fraud.Api.Scoring;

namespace InsurTech.Fraud.Api.Contracts;

// POST /v1/fraud/score (API spec §3.1.4 / LLD A.2.4)
public record ScoreRequest(
    Guid ClaimId,
    Guid PolicyId,
    string? CustomerId,
    string ClaimType,
    decimal ClaimedAmount,
    string? ClaimSummary);

public record ScoreResponse(
    double Score,
    string Decision,
    IReadOnlyList<ShapContribution> ShapTopN,
    string ModelVersion,
    bool DuplicateSuspected,
    long LatencyMs);

// Fraud & Risk Alerts work-queue item (Adjuster MFE)
public record FraudCaseResponse(
    string CaseId,
    string ClaimId,
    string PolicyId,
    string Status,
    string Severity,
    double InitialScore,
    bool DuplicateSuspected,
    string ModelVersion,
    IReadOnlyList<ShapContribution> ShapTopN,
    string ClaimSummary,
    string? DecisionReason,
    DateTimeOffset OpenedUtc);

public record CaseDecisionRequest(string Outcome, string Reason, string? InvestigatorUserId);
