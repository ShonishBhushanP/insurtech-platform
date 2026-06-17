namespace InsurTech.Policy.Api.Contracts;

// Request/response DTOs aligned with API Specification §3.1.6 (POST /v1/policies).

public record CoverageDto(string Code, decimal Limit);
public record PolicyholderDto(string UserId, string Name, string? KycRefId);
public record PremiumDto(decimal Base, decimal Tax, string Currency);

public record CreatePolicyRequest(
    string ProductCode,
    PolicyholderDto Policyholder,
    Dictionary<string, object>? InsuredItem,
    List<CoverageDto> Coverages,
    int TenureMonths,
    DateOnly StartDate,
    PremiumDto Premium);

public record PolicyResponse(
    string PolicyId,
    string PolicyNumber,
    string Status,
    DateTimeOffset IssuedAt,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    decimal SumInsured,
    string Currency,
    string ETag);
