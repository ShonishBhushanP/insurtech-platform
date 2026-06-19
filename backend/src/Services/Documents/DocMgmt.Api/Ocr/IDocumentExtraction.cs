using System.Text.Json;

namespace InsurTech.DocMgmt.Api.Ocr;

/// <summary>
/// OCR / form recognition over an uploaded document (deployment diagram: AI/ML — Azure Document
/// Intelligence). Azure adapter calls a Document Intelligence prebuilt model; the local stub
/// returns plausible fields so the pipeline is demonstrable offline. (No OpenAI involved.)
/// </summary>
public interface IDocumentExtraction
{
    string Engine { get; }
    Task<Dictionary<string, string>> ExtractAsync(string fileName, string mimeType, string sensitivityClass, string? documentUri, CancellationToken ct = default);
}

/// <summary>Local stub — canned fields by document kind. Used when Document Intelligence isn't configured.</summary>
public sealed class LocalExtractionStub : IDocumentExtraction
{
    public string Engine => "stub";

    public Task<Dictionary<string, string>> ExtractAsync(string fileName, string mimeType, string sensitivityClass, string? documentUri, CancellationToken ct = default)
    {
        var fields = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>
            {
                ["documentType"] = "PhotoOfDamage",
                ["damageDetected"] = "true",
                ["estimatedSeverity"] = "Moderate",
                ["vehiclePanel"] = "Rear bumper"
            }
            : new Dictionary<string, string>
            {
                ["documentType"] = sensitivityClass.Contains("Medical", StringComparison.OrdinalIgnoreCase) ? "MedicalBill" : "ClaimForm",
                ["claimantName"] = "R. Sharma",
                ["policyReference"] = "PL-2026-XXXXXX",
                ["totalAmount"] = "45000.00",
                ["currency"] = "INR"
            };
        fields["_sourceFile"] = fileName;
        return Task.FromResult(fields);
    }
}

/// <summary>
/// Azure Document Intelligence client (REST). Analyzes a document by URL with a prebuilt model and
/// returns extracted key/value pairs. Activated when <c>Azure:DocIntel:Endpoint</c>/<c>Key</c> are set.
/// </summary>
public sealed class AzureDocumentIntelligenceClient(HttpClient http, string apiKey, string model) : IDocumentExtraction
{
    public string Engine => $"azure-docintel:{model}";

    public async Task<Dictionary<string, string>> ExtractAsync(string fileName, string mimeType, string sensitivityClass, string? documentUri, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string> { ["_sourceFile"] = fileName };
        if (string.IsNullOrWhiteSpace(documentUri)) return result; // no blob to analyze (e.g. local mode)

        // Submit the analyze job.
        using var submit = new HttpRequestMessage(HttpMethod.Post,
            $"/documentintelligence/documentModels/{model}:analyze?api-version=2024-11-30")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { urlSource = documentUri }), System.Text.Encoding.UTF8, "application/json")
        };
        submit.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apiKey);
        var submitResp = await http.SendAsync(submit, ct);
        submitResp.EnsureSuccessStatusCode();
        var opLocation = submitResp.Headers.TryGetValues("Operation-Location", out var v) ? v.FirstOrDefault() : null;
        if (opLocation is null) return result;

        // Poll for completion (bounded).
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(1000, ct);
            using var poll = new HttpRequestMessage(HttpMethod.Get, opLocation);
            poll.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apiKey);
            var pollResp = await http.SendAsync(poll, ct);
            pollResp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await pollResp.Content.ReadAsStringAsync(ct));
            var status = doc.RootElement.GetProperty("status").GetString();
            if (status == "succeeded")
            {
                if (doc.RootElement.TryGetProperty("analyzeResult", out var ar))
                {
                    if (ar.TryGetProperty("keyValuePairs", out var kvps))
                        foreach (var kv in kvps.EnumerateArray())
                        {
                            var key = kv.GetProperty("key").GetProperty("content").GetString();
                            var val = kv.TryGetProperty("value", out var vEl) ? vEl.GetProperty("content").GetString() : null;
                            if (!string.IsNullOrWhiteSpace(key)) result[key!] = val ?? "";
                        }
                    if (ar.TryGetProperty("content", out var content))
                        result["_contentLength"] = (content.GetString()?.Length ?? 0).ToString();
                }
                return result;
            }
            if (status == "failed") return result;
        }
        return result;
    }
}
