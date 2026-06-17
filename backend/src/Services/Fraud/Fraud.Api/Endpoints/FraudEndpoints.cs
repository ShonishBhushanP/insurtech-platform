using System.Diagnostics;
using System.Text.Json;
using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;
using InsurTech.Fraud.Api.Contracts;
using InsurTech.Fraud.Api.Domain;
using InsurTech.Fraud.Api.Infrastructure;
using InsurTech.Fraud.Api.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InsurTech.Fraud.Api.Endpoints;

public sealed class FraudOptions
{
    public double BlockAt { get; set; } = 0.85;
    public double ReferAt { get; set; } = 0.55;
    public int DuplicateWindowMinutes { get; set; } = 10;
}

public static class FraudEndpoints
{
    public static void MapFraudEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /v1/fraud/score — synchronous risk score (≤200ms p95 budget in Azure; LLD A.2.3.1)
        app.MapPost("/v1/fraud/score", async (
            ScoreRequest req, FraudDbContext db, IFraudScoringEngine engine, IOptions<FraudOptions> opt, CancellationToken ct) =>
        {
            var sw = Stopwatch.StartNew();
            var o = opt.Value;

            // Duplicate / velocity feature: prior score for same policy + amount in the window.
            var since = DateTimeOffset.UtcNow.AddMinutes(-o.DuplicateWindowMinutes);
            var duplicate = await db.Scores.AnyAsync(s =>
                s.PolicyId == req.PolicyId && s.ClaimedAmount == req.ClaimedAmount && s.ScoredUtc >= since && s.ClaimId != req.ClaimId, ct);

            var result = await engine.ScoreAsync(req.ClaimType, req.ClaimedAmount, req.ClaimSummary ?? "", duplicate, ct);
            var decision = RiskScorer.Decide(result.Score, o.BlockAt, o.ReferAt);

            db.Scores.Add(new ScoreRecord
            {
                ClaimId = req.ClaimId, PolicyId = req.PolicyId,
                ClaimedAmount = req.ClaimedAmount, Score = result.Score, Decision = decision
            });

            // Open a case for anything not 'allow' (LLD A.2.3.2).
            if (decision != "allow")
            {
                var fraudCase = FraudCase.Open(req.ClaimId, req.PolicyId, result.Score, decision, duplicate,
                    engine.ModelVersion, JsonSerializer.Serialize(result.ShapTopN), req.ClaimSummary ?? "");
                db.Cases.Add(fraudCase);
            }
            await db.SaveChangesAsync(ct);

            sw.Stop();
            return Results.Ok(new ScoreResponse(result.Score, decision, result.ShapTopN,
                engine.ModelVersion, duplicate, sw.ElapsedMilliseconds));
        }).WithTags("Fraud");

        var cases = app.MapGroup("/v1/fraud/cases").WithTags("Fraud");

        // GET /v1/fraud/cases — Fraud & Risk Alerts work queue
        cases.MapGet("/", async (FraudDbContext db, string? status, CancellationToken ct) =>
        {
            var query = db.Cases.AsNoTracking();
            if (Enum.TryParse<FraudCaseStatus>(status, true, out var st)) query = query.Where(c => c.Status == st);
            var items = await query.OrderByDescending(c => c.InitialScore).ToListAsync(ct);
            return Results.Ok(items.Select(ToResponse));
        });

        cases.MapGet("/{id:guid}", async (Guid id, FraudDbContext db, CancellationToken ct) =>
        {
            var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return c is null ? Error.NotFound("FR-010", "Case not found.").ToProblem("fraud") : Results.Ok(ToResponse(c));
        });

        // POST /v1/fraud/cases/{id}/decision — investigator decision (closed-loop labeling)
        cases.MapPost("/{id:guid}/decision", async (Guid id, CaseDecisionRequest body, FraudDbContext db, CancellationToken ct) =>
        {
            var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Error.NotFound("FR-010", "Case not found.").ToProblem("fraud");
            if (!Enum.TryParse<FraudCaseStatus>(body.Outcome, true, out var outcome))
                return Error.Validation("FR-001", "outcome must be ConfirmedFraud or ConfirmedLegit.").ToProblem("fraud");
            try { c.Decide(outcome, body.Reason, body.InvestigatorUserId ?? "investigator"); }
            catch (InvalidOperationException ex) { return Error.Conflict("FR-020", ex.Message).ToProblem("fraud"); }
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(c));
        });
    }

    private static FraudCaseResponse ToResponse(FraudCase c)
    {
        var shap = JsonSerializer.Deserialize<List<ShapContribution>>(c.ShapJson) ?? new();
        return new FraudCaseResponse(
            c.Id.ToString(), c.ClaimId.ToString(), c.PolicyId.ToString(), c.Status.ToString(),
            c.Severity, c.InitialScore, c.DuplicateSuspected, c.ModelVersion, shap,
            c.ClaimSummary, c.DecisionReason, c.OpenedUtc);
    }
}
