using System.Net.Http.Json;
using InsurTech.Claims.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace InsurTech.Claims.Infrastructure.ExternalServices;

/// <summary>
/// Typed client for Payments & Settlement POST /v1/payments (LLD A.1.4 outbound, A.6).
/// Idempotency-Key carried so saga retries are safe (settlement is idempotency-critical).
/// </summary>
public sealed class PaymentClient(HttpClient http, ILogger<PaymentClient> logger) : IPaymentClient
{
    private record Wire(bool Captured, string Reference, string? FailureReason);

    public async Task<PaymentResult> CaptureAsync(PaymentRequest request, CancellationToken ct = default)
    {
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/v1/payments")
            {
                Content = JsonContent.Create(new
                {
                    claimId = request.ClaimId,
                    amount = request.Amount,
                    currency = request.Currency
                })
            };
            msg.Headers.Add("Idempotency-Key", request.IdempotencyKey);

            var response = await http.SendAsync(msg, ct);
            response.EnsureSuccessStatusCode();
            var wire = await response.Content.ReadFromJsonAsync<Wire>(cancellationToken: ct);

            return wire is null
                ? new PaymentResult(false, string.Empty, "empty payment response")
                : new PaymentResult(wire.Captured, wire.Reference, wire.FailureReason);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Payment capture failed for claim {ClaimId}", request.ClaimId);
            return new PaymentResult(false, string.Empty, "payments endpoint unavailable");
        }
    }
}
