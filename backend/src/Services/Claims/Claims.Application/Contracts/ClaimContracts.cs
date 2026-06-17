namespace InsurTech.Claims.Application.Contracts;

// DTOs aligned with API Specification §3.1.1 (POST /v1/claims) and §3.1.2 (GET status).

public record IncidentLocationDto(double Lat, double Lng, string Address);
public record AttachmentDto(string DocumentId, string Type);
public record FiledByDto(string UserId, string Channel);

public record FileClaimRequest(
    string PolicyId,
    string ClaimType,
    DateTimeOffset IncidentDate,
    IncidentLocationDto? IncidentLocation,
    string Description,
    decimal EstimatedAmount,
    string Currency,
    List<AttachmentDto>? Attachments,
    FiledByDto FiledBy);

public record FileClaimResponse(
    string ClaimId,
    string ClaimNumber,
    string Status,
    string StatusUrl,
    DateTimeOffset FiledAt);

public record ClaimStatusEntryDto(string Status, string Note, DateTimeOffset Timestamp);
public record ClaimDocumentDto(string DocumentId, string Type, bool Verified);

public record ClaimStatusResponse(
    string ClaimId,
    string ClaimNumber,
    string PolicyId,
    string ClaimType,
    string Status,
    decimal ClaimedAmount,
    decimal? ApprovedAmount,
    string Currency,
    double? FraudScore,
    string? FraudDecision,
    bool DocumentsVerified,
    string? DecisionReason,
    DateTimeOffset FiledAt,
    DateTimeOffset IncidentDate,
    IReadOnlyList<ClaimDocumentDto> Documents,
    IReadOnlyList<ClaimStatusEntryDto> History);
