using System.Net.Http.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InsurTech.Claims.Functions;

/// <summary>
/// Durable Functions implementation of the Claims adjudication saga (LLD A.1.3.2).
/// Flow: fraud score -> triage -> decide (rules) -> settle (Payments) -> mark paid.
/// Activities call the Fraud, Claims (internal endpoints) and Payments services over HTTP.
/// The in-process AdjudicationService remains the local default; this is the Azure-native path.
/// </summary>
public class AdjudicationFunctions(IHttpClientFactory httpFactory, IConfiguration config)
{
    private record ScoreWire(double Score, string Decision);
    private record PaymentWire(bool Captured, string Reference);

    private string Base(string key, string fallback) =>
        (config[key] ?? fallback).TrimEnd('/');

    // ---- HTTP starter: Claims POSTs the claim payload here to begin the orchestration ----
    [Function("StartAdjudication")]
    public async Task<HttpResponseData> Start(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "adjudications")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        FunctionContext ctx)
    {
        var logger = ctx.GetLogger("StartAdjudication");
        var input = await req.ReadFromJsonAsync<AdjudicationInput>();
        if (input is null)
            return req.CreateResponse(System.Net.HttpStatusCode.BadRequest);

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(AdjudicationOrchestrator), input);
        logger.LogInformation("Started adjudication orchestration {InstanceId} for claim {ClaimId}", instanceId, input.ClaimId);

        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    // ---- Orchestrator: deterministic control flow; all I/O is in activities ----
    [Function(nameof(AdjudicationOrchestrator))]
    public async Task AdjudicationOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<AdjudicationInput>()!;

        // 1) Fraud scoring
        var score = await context.CallActivityAsync<FraudResult>(nameof(ScoreFraud), input);

        // 2) Triage (records score + decision on the claim)
        await context.CallActivityAsync(nameof(Triage), new TriageInput(input.ClaimId, score.Score, score.Decision));

        // 3) Decide
        if (string.Equals(score.Decision, "block", StringComparison.OrdinalIgnoreCase))
            return; // diverted to investigation (UnderInvestigation); awaits human

        if (string.Equals(score.Decision, "refer", StringComparison.OrdinalIgnoreCase))
        {
            await context.CallActivityAsync(nameof(Refer),
                new ReferInput(input.ClaimId, $"Fraud risk score {score.Score:0.00} in refer band."));
            return; // awaits underwriter (a durable timer / external event would gate this in full impl)
        }

        if (input.ClaimedAmount > input.AutoApproveThreshold)
        {
            await context.CallActivityAsync(nameof(Refer),
                new ReferInput(input.ClaimId, $"Claimed amount {input.ClaimedAmount:N0} exceeds auto-approve threshold."));
            return;
        }

        // 4) Auto-approve + settle (happy path)
        await context.CallActivityAsync(nameof(Approve), new ApproveInput(input.ClaimId, input.ClaimedAmount, input.Currency));
        var payment = await context.CallActivityAsync<PaymentResult>(nameof(CapturePayment), input);
        if (payment.Captured)
            await context.CallActivityAsync(nameof(MarkPaid), new MarkPaidInput(input.ClaimId, payment.Reference));
    }

    // ---- Activities (I/O) ----

    [Function(nameof(ScoreFraud))]
    public async Task<FraudResult> ScoreFraud([ActivityTrigger] AdjudicationInput input)
    {
        try
        {
            var http = httpFactory.CreateClient();
            var resp = await http.PostAsJsonAsync(Base("Services:Fraud", "http://localhost:5103") + "/v1/fraud/score", new
            {
                claimId = input.ClaimId,
                policyId = input.PolicyId,
                customerId = input.CustomerId,
                claimType = input.ClaimType,
                claimedAmount = input.ClaimedAmount,
                claimSummary = input.Summary
            });
            resp.EnsureSuccessStatusCode();
            var wire = await resp.Content.ReadFromJsonAsync<ScoreWire>();
            return wire is null ? new FraudResult(0.5, "refer") : new FraudResult(wire.Score, wire.Decision);
        }
        catch
        {
            return new FraudResult(0.5, "refer"); // fail-open (FR-050)
        }
    }

    [Function(nameof(Triage))]
    public Task Triage([ActivityTrigger] TriageInput t) =>
        Post($"/internal/claims/{t.ClaimId}/triage", new { score = t.Score, decision = t.Decision });

    [Function(nameof(Refer))]
    public Task Refer([ActivityTrigger] ReferInput r) =>
        Post($"/internal/claims/{r.ClaimId}/refer", new { reason = r.Reason });

    [Function(nameof(Approve))]
    public Task Approve([ActivityTrigger] ApproveInput a) =>
        Post($"/internal/claims/{a.ClaimId}/approve", new { amount = a.Amount, currency = a.Currency });

    [Function(nameof(MarkPaid))]
    public Task MarkPaid([ActivityTrigger] MarkPaidInput m) =>
        Post($"/internal/claims/{m.ClaimId}/paid", new { reference = m.Reference });

    [Function(nameof(CapturePayment))]
    public async Task<PaymentResult> CapturePayment([ActivityTrigger] AdjudicationInput input)
    {
        try
        {
            var http = httpFactory.CreateClient();
            using var msg = new HttpRequestMessage(HttpMethod.Post, Base("Services:Payments", "http://localhost:5105") + "/v1/payments")
            {
                Content = JsonContent.Create(new { claimId = input.ClaimId, amount = input.ClaimedAmount, currency = input.Currency })
            };
            msg.Headers.Add("Idempotency-Key", input.ClaimId.ToString("N"));
            var resp = await http.SendAsync(msg);
            resp.EnsureSuccessStatusCode();
            var wire = await resp.Content.ReadFromJsonAsync<PaymentWire>();
            return wire is null ? new PaymentResult(false, "") : new PaymentResult(wire.Captured, wire.Reference);
        }
        catch
        {
            return new PaymentResult(false, "");
        }
    }

    // POST a JSON body to the Claims service internal saga endpoint.
    private async Task Post(string path, object body)
    {
        var http = httpFactory.CreateClient();
        var resp = await http.PostAsJsonAsync(Base("Services:Claims", "http://localhost:5102") + path, body);
        resp.EnsureSuccessStatusCode();
    }
}
