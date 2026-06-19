using System.Collections.Concurrent;
using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;

// Underwriting service (LLD A.5). Rule-based auto-decisions + referral to a senior underwriter.
// Called by Policy Management during issuance (request-reply in Azure). Local: in-memory.

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);

var app = builder.Build();
app.UseInsurTechDefaults();

var decisions = new ConcurrentDictionary<string, DecisionResponse>();

// Simple rate card by product family (per-mille of sum insured).
decimal RateFor(string productCode) => productCode.ToUpperInvariant() switch
{
    var p when p.StartsWith("MOTOR") => 0.0198m,
    var p when p.StartsWith("HEALTH") => 0.0284m,
    var p when p.StartsWith("PROPERTY") => 0.0125m,
    _ => 0.0225m
};

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "underwriting" }));

// POST /v1/underwriting/decisions — auto-decision with referral threshold
app.MapPost("/v1/underwriting/decisions", (UwDecisionRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ProductCode) || req.SumInsured <= 0)
        return Error.Validation("UW-001", "productCode and a positive sumInsured are required.").ToProblem("underwriting");

    // Rule engine: refer high exposure or high applicant risk; else auto-accept with a quoted premium.
    var risk = req.ApplicantRiskScore ?? 0.0;
    string outcome; string reason;
    if (req.SumInsured > 5_000_000m || risk >= 0.75)
    { outcome = "Refer"; reason = "Exposure or applicant risk exceeds auto-accept band — senior underwriter review."; }
    else if (risk >= 0.95)
    { outcome = "Decline"; reason = "Applicant risk above acceptable ceiling."; }
    else
    { outcome = "Accept"; reason = "Within auto-acceptance limits."; }

    var rate = RateFor(req.ProductCode);
    var premium = decimal.Round(req.SumInsured * rate, 2);

    var resp = new DecisionResponse($"uwd_{Guid.NewGuid():N}"[..16], outcome, reason,
        rate, outcome == "Accept" ? premium : null, DateTimeOffset.UtcNow);
    decisions[resp.DecisionId] = resp;
    return Results.Ok(resp);
}).WithTags("Underwriting");

app.MapGet("/v1/underwriting/decisions/{id}", (string id) =>
    decisions.TryGetValue(id, out var d) ? Results.Ok(d)
        : Error.NotFound("UW-010", "Decision not found.").ToProblem("underwriting")
).WithTags("Underwriting");

// POST /v1/underwriting/referrals — queue a referred case for a senior underwriter
app.MapPost("/v1/underwriting/referrals", (ReferralRequest req) =>
    Results.Ok(new { referralId = $"uwr_{Guid.NewGuid():N}"[..16], status = "Queued", req.PolicyRef, req.Reason })
).WithTags("Underwriting");

app.Run();

record UwDecisionRequest(string ProductCode, decimal SumInsured, double? ApplicantRiskScore);
record DecisionResponse(string DecisionId, string Outcome, string Reason, decimal RatePerMille, decimal? QuotedPremium, DateTimeOffset DecidedUtc);
record ReferralRequest(string PolicyRef, string Reason);
