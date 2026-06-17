using System.Text.Json;
using InsurTech.BuildingBlocks.Idempotency;
using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;
using InsurTech.Claims.Application.Abstractions;
using InsurTech.Claims.Application.Commands;
using InsurTech.Claims.Application.Contracts;
using InsurTech.Claims.Application.Queries;
using InsurTech.Claims.Domain;
using InsurTech.Claims.Domain.ValueObjects;

namespace InsurTech.Claims.Api.Endpoints;

public static class ClaimsEndpoints
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    public static void MapClaimsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/claims").WithTags("Claims");

        // POST /v1/claims — File New Claim (FNOL). Idempotency-Key required (API spec §3.1.1).
        group.MapPost("/", async (
            FileClaimRequest req, HttpRequest http,
            FileClaimHandler handler, IIdempotencyStore idempotency, CancellationToken ct) =>
        {
            var key = http.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key))
                return Error.Validation("CLM-001", "Idempotency-Key header is required.").ToProblem("claims");

            if (await idempotency.GetAsync(key, ct) is { } cached)
                return Results.Json(JsonSerializer.Deserialize<FileClaimResponse>(cached), statusCode: 202);

            var result = await handler.HandleAsync(req, ct);
            if (result.IsFailure) return result.Error.ToProblem("claims");

            await idempotency.SetAsync(key, JsonSerializer.Serialize(result.Value), IdempotencyTtl, ct);
            return Results.Accepted($"/v1/claims/{result.Value!.ClaimId}/status", result.Value);
        });

        // GET /v1/claims — adjuster work queue / recent claims dashboard
        group.MapGet("/", async (IClaimRepository repo, string? policyId, string? userId, string? status, CancellationToken ct) =>
        {
            Guid? pid = Guid.TryParse(policyId, out var p) ? p : null;
            ClaimStatus? st = Enum.TryParse<ClaimStatus>(status, true, out var s) ? s : null;
            var claims = await repo.ListAsync(pid, userId, st, ct);
            return Results.Ok(claims.Select(c => c.ToStatusResponse()));
        });

        // GET /v1/claims/{id} and /status
        group.MapGet("/{id:guid}", GetClaim);
        group.MapGet("/{id:guid}/status", GetClaim);

        // POST /v1/claims/{id}/cancel
        group.MapPost("/{id:guid}/cancel", async (Guid id, IClaimRepository repo, CancellationToken ct) =>
        {
            var claim = await repo.GetAsync(id, ct);
            if (claim is null) return Error.NotFound("CLM-010", "Claim not found.").ToProblem("claims");
            try { claim.Cancel(); }
            catch (InvalidOperationException ex) { return Error.Conflict("CLM-021", ex.Message).ToProblem("claims"); }
            await repo.SaveChangesAsync(ct);
            return Results.Ok(claim.ToStatusResponse());
        });

        // POST /v1/claims/{id}/decision — Adjuster Workbench manual decision (staff)
        group.MapPost("/{id:guid}/decision", async (
            Guid id, AdjusterDecisionRequest body,
            IClaimRepository repo, IPaymentClient payments, CancellationToken ct) =>
        {
            var claim = await repo.GetAsync(id, ct);
            if (claim is null) return Error.NotFound("CLM-010", "Claim not found.").ToProblem("claims");

            try
            {
                switch (body.Outcome?.ToLowerInvariant())
                {
                    case "approve":
                        var amount = body.ApprovedAmount ?? claim.ClaimedAmount;
                        claim.Approve(Money.Of(amount, claim.CurrencyCode));
                        await repo.SaveChangesAsync(ct);
                        var pay = await payments.CaptureAsync(new PaymentRequest(claim.Id, amount, claim.CurrencyCode, claim.Id.ToString("N")), ct);
                        if (pay.Captured) { claim.MarkPaid(pay.Reference); await repo.SaveChangesAsync(ct); }
                        break;
                    case "reject":
                        claim.Reject(body.Reason ?? "Rejected by adjuster.");
                        await repo.SaveChangesAsync(ct);
                        break;
                    default:
                        return Error.Validation("CLM-001", "outcome must be 'approve' or 'reject'.").ToProblem("claims");
                }
            }
            catch (InvalidOperationException ex) { return Error.Conflict("CLM-021", ex.Message).ToProblem("claims"); }

            return Results.Ok(claim.ToStatusResponse());
        });
    }

    private static async Task<IResult> GetClaim(Guid id, IClaimRepository repo, CancellationToken ct)
    {
        var claim = await repo.GetAsync(id, ct);
        return claim is null
            ? Error.NotFound("CLM-010", "Claim not found.").ToProblem("claims")
            : Results.Ok(claim.ToStatusResponse());
    }
}

public record AdjusterDecisionRequest(string Outcome, decimal? ApprovedAmount, string? Reason);
