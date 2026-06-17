using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;

// Partner Integration service (LLD A.7 / API spec §3.1.3). Cashless pre-authorization for
// network hospitals/garages (B2B). In Azure this is reached only over mTLS via a Private Link
// Service; the mTLS/cert plumbing is terminated at App Gateway. Here we run a simple sync
// rules engine that returns an authorization decision within the sum-insured cap.

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);

var app = builder.Build();
app.UseInsurTechDefaults();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "partner" }));

// POST /v1/partner/cashless/authorize
app.MapPost("/v1/partner/cashless/authorize", (CashlessRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.PartnerId) || string.IsNullOrWhiteSpace(req.PolicyId))
        return Error.Validation("PRT-001", "partnerId and policyId are required.").ToProblem("partner");
    if (req.EstimatedAmount <= 0)
        return Error.Validation("PRT-001", "estimatedAmount must be greater than zero.").ToProblem("partner");

    // Simple cap/co-pay rules engine (Underwriting tariff in Azure).
    const decimal sumInsuredCap = 1_000_000m;
    const decimal coPayRate = 0.10m;

    var approvedBeforeCap = Math.Min(req.EstimatedAmount, sumInsuredCap);
    var coPay = decimal.Round(approvedBeforeCap * coPayRate, 2);
    var approved = approvedBeforeCap - coPay;

    var status = approved >= req.EstimatedAmount - coPay ? "Approved" : "PartiallyApproved";

    var response = new CashlessResponse(
        $"auth_{Guid.NewGuid():N}"[..13],
        status,
        approved,
        coPay,
        req.Currency,
        DateTimeOffset.UtcNow.AddDays(7),
        "00",
        $"{status} — within sum insured (co-pay {coPayRate:P0}).",
        $"clm_partner_{Guid.NewGuid():N}"[..12],
        "T+2 business days after final bill submission.");

    return Results.Ok(response);
}).WithTags("Partner");

app.Run();

record CashlessRequest(string PartnerId, string MemberId, string PolicyId, string TreatmentCode,
    decimal EstimatedAmount, string Currency, string? PreAuthRefId, string? FacilityType, DateTimeOffset? AdmissionDate);

record CashlessResponse(string AuthorizationId, string AuthorizationStatus, decimal ApprovedAmount,
    decimal CoPayAmount, string Currency, DateTimeOffset ValidUntil, string ResponseCode,
    string ResponseMessage, string ReferenceClaimId, string SettlementSla);
