using InsurTech.BuildingBlocks.Results;
using InsurTech.Claims.Application.Abstractions;
using InsurTech.Claims.Application.Contracts;
using InsurTech.Claims.Domain;
using InsurTech.Claims.Domain.Aggregates;
using InsurTech.Claims.Domain.ValueObjects;

namespace InsurTech.Claims.Application.Commands;

/// <summary>
/// Use case: file a new claim (FNOL). Persists the Claim aggregate + outbox row in one
/// transaction (LLD A.1.3.1), returning 202-style ack. Adjudication runs asynchronously.
/// </summary>
public sealed class FileClaimHandler(IClaimRepository repository)
{
    public async Task<Result<FileClaimResponse>> HandleAsync(FileClaimRequest req, CancellationToken ct = default)
    {
        if (!Guid.TryParse(req.PolicyId, out var policyId))
            return Error.Validation("CLM-001", "policyId must be a valid identifier.");

        if (!Enum.TryParse<ClaimType>(req.ClaimType, ignoreCase: true, out var claimType))
            return Error.Validation("CLM-001", $"claimType '{req.ClaimType}' is not supported.");

        if (req.EstimatedAmount <= 0)
            return Error.Validation("CLM-001", "estimatedAmount must be greater than zero.");

        if (req.IncidentDate > DateTimeOffset.UtcNow)
            return Error.Validation("CLM-001", "incidentDate cannot be in the future.");

        if (string.IsNullOrWhiteSpace(req.Description) || req.Description.Length > 2000)
            return Error.Validation("CLM-001", "description is required and must be <= 2000 chars.");

        Money claimed;
        try { claimed = Money.Of(req.EstimatedAmount, req.Currency); }
        catch (Exception ex) { return Error.Validation("CLM-001", ex.Message); }

        var documents = (req.Attachments ?? new())
            .Select(a => new ClaimDocument(a.DocumentId, a.Type));

        var claim = Claim.File(
            policyId, claimType, req.FiledBy.UserId,
            req.IncidentDate, req.Description, req.IncidentLocation?.Address, claimed, documents);

        repository.Add(claim);
        await repository.SaveChangesAsync(ct);

        return new FileClaimResponse(
            claim.Id.ToString(), claim.ClaimNumber, claim.Status.ToString(),
            $"/v1/claims/{claim.Id}/status", claim.FiledUtc);
    }
}
