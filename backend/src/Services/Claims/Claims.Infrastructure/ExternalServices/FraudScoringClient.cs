using System.Net.Http.Json;
using InsurTech.Claims.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace InsurTech.Claims.Infrastructure.ExternalServices;

/// <summary>
/// Typed client for Fraud Detection POST /v1/fraud/score (LLD A.1.4 outbound, A.2.4).
/// Fail-open (FR-050): on timeout / outage the FNOL saga proceeds with decision=refer so a
/// fraud outage never blocks claim filing; a case is opened for manual review instead.
/// </summary>
public sealed class FraudScoringClient(HttpClient http, ILogger<FraudScoringClient> logger) : IFraudScoringClient
{
    private record Wire(double Score, string Decision, List<ShapContribution> ShapTopN, string ModelVersion);

    public async Task<FraudScoreResult> ScoreAsync(FraudScoreRequest request, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                claimId = request.ClaimId,
                policyId = request.PolicyId,
                customerId = request.CustomerId,
                claimType = request.ClaimType.ToString(),
                claimedAmount = request.ClaimedAmount,
                claimSummary = request.Summary
            };

            var response = await http.PostAsJsonAsync("/v1/fraud/score", payload, ct);
            response.EnsureSuccessStatusCode();
            var wire = await response.Content.ReadFromJsonAsync<Wire>(cancellationToken: ct);

            return wire is null
                ? FailOpen("empty fraud response")
                : new FraudScoreResult(wire.Score, wire.Decision, wire.ShapTopN ?? new(), wire.ModelVersion);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fraud scoring unavailable for claim {ClaimId}; failing open to 'refer'", request.ClaimId);
            return FailOpen("fraud endpoint unavailable");
        }
    }

    private static FraudScoreResult FailOpen(string reason) =>
        new(0.5, "refer", new[] { new ShapContribution(reason, 0) }, "fail-open");
}
