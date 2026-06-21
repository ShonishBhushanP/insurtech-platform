using System.Text;
using System.Text.Json;

namespace InsurTech.DocMgmt.Api.Ocr;

/// <summary>
/// OCR / form recognition over an uploaded document (deployment diagram: AI/ML — Azure Document
/// Intelligence). Azure adapter calls a Document Intelligence prebuilt model on the real bytes;
/// the local stub derives genuine attributes from the bytes (format, dimensions) + verifies that
/// the content matches the declared type. No OpenAI involved.
/// </summary>
public interface IDocumentExtraction
{
    string Engine { get; }
    Task<Dictionary<string, string>> ExtractAsync(string fileName, string mimeType, string sensitivityClass,
        string? documentUri, byte[]? content, CancellationToken ct = default);
}

/// <summary>Helpers to probe image/file bytes without an image library (PNG/JPEG/PDF headers).</summary>
public static class FileProbe
{
    public static (string Format, int Width, int Height) Probe(byte[] b)
    {
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) // PNG
            return ("PNG", (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);

        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) // JPEG — scan for SOF marker
        {
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var marker = b[i + 1];
                if (marker >= 0xC0 && marker <= 0xC3)
                    return ("JPEG", (b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                i += 2 + ((b[i + 2] << 8) | b[i + 3]);
            }
            return ("JPEG", 0, 0);
        }
        if (b.Length >= 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46) return ("PDF", 0, 0); // %PDF
        return ("unknown", 0, 0);
    }

    public static bool MatchesMime(string format, string mimeType) => (format, mimeType.ToLowerInvariant()) switch
    {
        ("PNG", "image/png") => true,
        ("JPEG", "image/jpeg" or "image/jpg") => true,
        ("PDF", "application/pdf") => true,
        _ => false
    };
}

/// <summary>Local stub — derives real attributes from the bytes + canned damage assessment.</summary>
public sealed class LocalExtractionStub : IDocumentExtraction
{
    public string Engine => "stub";

    public Task<Dictionary<string, string>> ExtractAsync(string fileName, string mimeType, string sensitivityClass,
        string? documentUri, byte[]? content, CancellationToken ct = default)
    {
        var f = new Dictionary<string, string>();
        var isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        if (content is { Length: > 0 })
        {
            var (format, w, h) = FileProbe.Probe(content);
            var verified = FileProbe.MatchesMime(format, mimeType);
            f["detectedFormat"] = format;
            f["documentTypeVerified"] = verified ? "true" : "false (content does not match declared type)";
            f["fileSize"] = $"{content.Length / 1024} KB";
            if (w > 0 && h > 0)
            {
                f["imageDimensions"] = $"{w} x {h} px";
                f["imageQuality"] = w >= 1024 ? "High" : "Standard";
            }
        }
        else
        {
            f["documentTypeVerified"] = "unverified (no content)";
        }

        // The stub can't read pixels, so it classifies the document from its file name + sensitivity
        // class (the engine's stand-in for "what is this document?") and emits fields appropriate to
        // that kind — instead of always returning vehicle-damage data. A real Document Intelligence
        // model would derive these from the content itself.
        foreach (var kv in FieldsFor(Classify(fileName, sensitivityClass, isImage)))
            f[kv.Key] = kv.Value;

        f["_sourceFile"] = fileName;
        return Task.FromResult(f);
    }

    private static string Classify(string fileName, string sensitivityClass, bool isImage)
    {
        var n = fileName.ToLowerInvariant();
        bool Has(params string[] terms) => terms.Any(t => n.Contains(t));

        if (Has("death", "demise") || (Has("life") && Has("certificate", "cert"))) return "DeathCertificate";
        if (Has("medical", "hospital", "discharge", "diagnos", "prescription")
            || sensitivityClass.Contains("Medical", StringComparison.OrdinalIgnoreCase)) return "MedicalReport";
        if (Has("property", "fire", "flood", "burglary", "home")) return "PropertyDamage";
        if (Has("damage", "motor", "vehicle", "accident", "dent", "collision", "bumper", "car")) return "MotorDamage";
        if (Has("aadhaar", "aadhar", "pan", "passport", "license", "licence", "idproof", "kyc")) return "IdProof";
        if (Has("invoice", "bill", "receipt", "estimate", "quotation")) return "Invoice";
        return isImage ? "PhotoEvidence" : "ClaimForm";
    }

    private static Dictionary<string, string> FieldsFor(string kind) => kind switch
    {
        "DeathCertificate" => new()
        {
            ["documentType"] = "DeathCertificate",
            ["deceasedName"] = "R. Sharma",
            ["dateOfDeath"] = "2026-05-28",
            ["certificateNumber"] = "DC-KA-2026-018452",
            ["issuingAuthority"] = "Registrar of Births & Deaths, Bengaluru",
            ["registrationDate"] = "2026-06-02",
        },
        "MedicalReport" => new()
        {
            ["documentType"] = "MedicalReport",
            ["patientName"] = "R. Sharma",
            ["hospitalName"] = "Manipal Hospital, Bengaluru",
            ["admissionDate"] = "2026-06-04",
            ["dischargeDate"] = "2026-06-09",
            ["primaryDiagnosis"] = "Acute appendicitis (K35.80)",
            ["billedAmount"] = "1,82,400",
            ["currency"] = "INR",
        },
        "PropertyDamage" => new()
        {
            ["documentType"] = "PhotoOfDamage",
            ["damageDetected"] = "true",
            ["perilType"] = "Fire / smoke",
            ["affectedArea"] = "Kitchen and adjoining wall",
            ["estimatedSeverity"] = "Major",
            ["estimatedRepairCost"] = "INR 2,10,000 - 2,80,000",
        },
        "MotorDamage" => new()
        {
            ["documentType"] = "PhotoOfDamage",
            ["damageDetected"] = "true",
            ["estimatedSeverity"] = "Moderate",
            ["vehiclePanel"] = "Rear bumper",
            ["estimatedRepairCost"] = "INR 38,000 - 52,000",
        },
        "IdProof" => new()
        {
            ["documentType"] = "IdentityProof",
            ["holderName"] = "R. Sharma",
            ["idType"] = "Aadhaar",
            ["idNumber"] = "XXXX XXXX 4521",
        },
        "Invoice" => new()
        {
            ["documentType"] = "Invoice",
            ["vendorName"] = "City Auto Works",
            ["invoiceNumber"] = "INV-2026-7741",
            ["totalAmount"] = "47,300",
            ["currency"] = "INR",
        },
        "PhotoEvidence" => new()
        {
            ["documentType"] = "PhotoEvidence",
            ["note"] = "Supporting photograph attached to the claim. No structured fields extracted.",
        },
        _ => new()
        {
            ["documentType"] = "ClaimForm",
            ["claimantName"] = "R. Sharma",
            ["policyReference"] = "PL-2026-XXXXXX",
            ["totalAmount"] = "45000.00",
            ["currency"] = "INR",
        },
    };
}

/// <summary>
/// Azure Document Intelligence client (REST). Analyzes the document with a prebuilt model — by URL
/// (blob SAS) or by the raw bytes (base64Source) — and returns extracted key/value pairs.
/// Activated when <c>Azure:DocIntel:Endpoint</c>/<c>Key</c> are set.
/// </summary>
public sealed class AzureDocumentIntelligenceClient(HttpClient http, string apiKey, string model) : IDocumentExtraction
{
    public string Engine => $"azure-docintel:{model}";

    public async Task<Dictionary<string, string>> ExtractAsync(string fileName, string mimeType, string sensitivityClass,
        string? documentUri, byte[]? content, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string> { ["_sourceFile"] = fileName };

        object body;
        if (!string.IsNullOrWhiteSpace(documentUri)) body = new { urlSource = documentUri };
        else if (content is { Length: > 0 }) body = new { base64Source = Convert.ToBase64String(content) };
        else return result;

        using var submit = new HttpRequestMessage(HttpMethod.Post,
            $"/documentintelligence/documentModels/{model}:analyze?api-version=2024-11-30")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        submit.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apiKey);
        var submitResp = await http.SendAsync(submit, ct);
        submitResp.EnsureSuccessStatusCode();
        var opLocation = submitResp.Headers.TryGetValues("Operation-Location", out var v) ? v.FirstOrDefault() : null;
        if (opLocation is null) return result;

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
                    if (ar.TryGetProperty("content", out var contentEl))
                        result["_contentLength"] = (contentEl.GetString()?.Length ?? 0).ToString();
                }
                return result;
            }
            if (status == "failed") return result;
        }
        return result;
    }
}
