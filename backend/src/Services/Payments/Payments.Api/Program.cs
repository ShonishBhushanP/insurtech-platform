using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;

// Payments & Settlement service (LLD A.6 / API spec §3.1.7). Idempotency-critical:
// the same Idempotency-Key must capture once-and-only-once (double-capture is a P1 incident).
// Provider call (Razorpay / HDFC NEFT) is stubbed; a Confidential-Ledger receipt hash is simulated.

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);

var app = builder.Build();
app.UseInsurTechDefaults();

var captured = new ConcurrentDictionary<string, PaymentResponse>(); // Idempotency-Key -> response

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payments" }));

// POST /v1/payments — Capture Payment
app.MapPost("/v1/payments", (CapturePaymentRequest req, HttpRequest http) =>
{
    var idemKey = http.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(idemKey))
        return Error.Validation("PAY-001", "Idempotency-Key header is mandatory.").ToProblem("payments");
    if (req.Amount <= 0)
        return Error.Validation("PAY-001", "amount must be greater than zero.").ToProblem("payments");

    // Once-and-only-once: replay returns the original capture, never a second one.
    if (captured.TryGetValue(idemKey, out var existing))
        return Results.Ok(existing);

    var paymentId = $"pay_{Guid.NewGuid():N}"[..16];
    var providerTxn = $"NEFT{Random.Shared.Next(10000000, 99999999)}";
    var capturedAt = DateTimeOffset.UtcNow;
    var receiptHash = Sha256($"{req.ClaimId}|{req.Amount}|{req.Currency}|{providerTxn}|{capturedAt:O}");

    var response = new PaymentResponse(true, paymentId, "Captured", providerTxn,
        req.Amount, req.Currency, capturedAt, receiptHash,
        capturedAt.AddDays(2), null);

    captured[idemKey] = response;
    return Results.Ok(response);
}).WithTags("Payments");

app.Run();

static string Sha256(string input) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

record CapturePaymentRequest(Guid ClaimId, Guid? PolicyId, decimal Amount, string Currency,
    string? Purpose, string? SettlementMode);

// 'Captured' field lets the Claims typed client read a simple bool; full fields mirror API spec §3.1.7.2.
record PaymentResponse(bool Captured, string PaymentId, string PaymentStatus, string ProviderTxnId,
    decimal Amount, string Currency, DateTimeOffset CapturedAt, string LedgerReceiptHash,
    DateTimeOffset SettlementEta, string? FailureReason)
{
    public string Reference => ProviderTxnId;
}
