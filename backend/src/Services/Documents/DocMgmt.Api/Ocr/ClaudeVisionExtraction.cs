using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace InsurTech.DocMgmt.Api.Ocr;

/// <summary>
/// Vision-based OCR / form recognition using Claude's multimodal Messages API. Sends the actual
/// uploaded bytes (image or PDF) to Claude and asks for the fields it can read, returned as a flat
/// JSON object — so a death certificate yields the real deceased name, dates and certificate number
/// instead of canned values. Substitutes for Azure Document Intelligence in the AI/ML tier; the
/// byte-probe attributes (format, dimensions, type-verify) are still derived locally and merged in.
/// Falls back to the local stub if no content is sent or the call fails.
/// </summary>
public sealed class ClaudeVisionExtraction(HttpClient http, string apiKey, string model, ILogger<ClaudeVisionExtraction> logger)
    : IDocumentExtraction
{
    public string Engine => $"claude-vision:{model}";

    private static readonly string[] SupportedImage = { "image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp" };

    private const string Prompt =
        "You are an OCR / form-recognition engine for an insurance claims system. Read the attached " +
        "document and extract every field you can see. Respond with ONLY a single flat JSON object " +
        "(no markdown, no prose) whose keys are camelCase field names and whose values are the exact " +
        "text read from the document. Always include a \"documentType\" key (e.g. DeathCertificate, " +
        "MedicalReport, Invoice, IdentityProof, PhotoOfDamage, ClaimForm). Use null for fields that " +
        "are present but unreadable. Do not invent values that are not in the document.";

    public async Task<Dictionary<string, string>> ExtractAsync(string fileName, string mimeType, string sensitivityClass,
        string? documentUri, byte[]? content, CancellationToken ct = default)
    {
        // Locally-derived, trustworthy byte attributes (format/dimensions/type-verify).
        var f = new Dictionary<string, string>();
        if (content is { Length: > 0 })
        {
            var (format, w, h) = FileProbe.Probe(content);
            f["detectedFormat"] = format;
            f["documentTypeVerified"] = FileProbe.MatchesMime(format, mimeType) ? "true" : "false (content does not match declared type)";
            f["fileSize"] = $"{content.Length / 1024} KB";
            if (w > 0 && h > 0)
            {
                f["imageDimensions"] = $"{w} x {h} px";
                f["imageQuality"] = w >= 1024 ? "High" : "Standard";
            }
        }

        var read = content is { Length: > 0 } ? await ReadWithClaudeAsync(mimeType, content, ct) : null;
        if (read is null)
        {
            // Couldn't send/parse — degrade to the canned stub so the pipeline still completes.
            var fallback = await new LocalExtractionStub().ExtractAsync(fileName, mimeType, sensitivityClass, documentUri, content, ct);
            foreach (var kv in fallback) f.TryAdd(kv.Key, kv.Value);
            f["_extractionNote"] = "vision unavailable — showing representative values";
            f["_sourceFile"] = fileName;
            return f;
        }

        foreach (var kv in read) f[kv.Key] = kv.Value;
        f["_sourceFile"] = fileName;
        return f;
    }

    private async Task<Dictionary<string, string>?> ReadWithClaudeAsync(string mimeType, byte[] content, CancellationToken ct)
    {
        try
        {
            var mt = mimeType.ToLowerInvariant();
            object source;
            if (mt == "application/pdf")
                source = new { type = "base64", media_type = "application/pdf", data = Convert.ToBase64String(content) };
            else if (SupportedImage.Contains(mt))
                source = new { type = "base64", media_type = mt == "image/jpg" ? "image/jpeg" : mt, data = Convert.ToBase64String(content) };
            else
                return null; // unsupported media for vision

            var docBlockType = mt == "application/pdf" ? "document" : "image";
            var payload = new
            {
                model,
                max_tokens = 1024,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = docBlockType, source },
                            new { type = "text", text = Prompt }
                        }
                    }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
            return ParseFields(text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Claude vision extraction failed; falling back to stub");
            return null;
        }
    }

    // Parse Claude's reply (a flat JSON object, possibly fenced) into string key/values.
    private static Dictionary<string, string>? ParseFields(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        s = s[start..(end + 1)];

        try
        {
            using var doc = JsonDocument.Parse(s);
            var result = new Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var v = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Null => null,
                    JsonValueKind.Number => p.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => p.Value.GetRawText()
                };
                if (!string.IsNullOrWhiteSpace(v)) result[p.Name] = v!;
            }
            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }
}
