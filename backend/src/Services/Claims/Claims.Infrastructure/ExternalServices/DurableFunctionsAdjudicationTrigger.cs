using System.Net.Http.Json;
using InsurTech.Claims.Application.Abstractions;
using InsurTech.Claims.Application.Adjudication;
using Microsoft.Extensions.Logging;

namespace InsurTech.Claims.Infrastructure.ExternalServices;

/// <summary>
/// Delegates the adjudication saga to the Azure Durable Functions orchestrator (Claims.Functions)
/// by POSTing the claim payload to its HTTP starter. Used when
/// <c>Claims:Adjudication:Mode = DurableFunctions</c> and a Functions base URL is configured.
/// </summary>
public sealed class DurableFunctionsAdjudicationTrigger(
    HttpClient http, IClaimRepository repository, AdjudicationOptions options,
    ILogger<DurableFunctionsAdjudicationTrigger> logger) : IAdjudicationTrigger
{
    public async Task TriggerAsync(Guid claimId, CancellationToken ct = default)
    {
        var claim = await repository.GetAsync(claimId, ct);
        if (claim is null) { logger.LogWarning("Adjudication trigger: claim {ClaimId} not found", claimId); return; }

        var input = new AdjudicationInput(
            claim.Id, claim.PolicyId, claim.FiledByUserId, claim.Type.ToString(),
            claim.ClaimedAmount, claim.CurrencyCode, claim.IncidentDescription, options.AutoApproveThreshold);

        var response = await http.PostAsJsonAsync("/api/adjudications", input, ct);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Started Durable adjudication orchestration for claim {ClaimId}", claimId);
    }
}
