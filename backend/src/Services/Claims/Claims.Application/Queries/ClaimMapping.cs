using InsurTech.Claims.Application.Contracts;
using InsurTech.Claims.Domain.Aggregates;

namespace InsurTech.Claims.Application.Queries;

public static class ClaimMapping
{
    public static ClaimStatusResponse ToStatusResponse(this Claim c) => new(
        c.Id.ToString(),
        c.ClaimNumber,
        c.PolicyId.ToString(),
        c.Type.ToString(),
        c.Status.ToString(),
        c.ClaimedAmount,
        c.ApprovedAmount,
        c.CurrencyCode,
        c.FraudScore,
        c.FraudDecision,
        c.DocumentsVerified,
        c.DecisionReason,
        c.FiledUtc,
        c.IncidentDate,
        c.Documents.Select(d => new ClaimDocumentDto(d.DocumentId, d.Type, c.DocumentsVerified)).ToList(),
        c.History.Select(h => new ClaimStatusEntryDto(h.Status, h.Note, h.Timestamp)).ToList());
}
