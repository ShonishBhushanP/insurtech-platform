namespace InsurTech.DocMgmt.Api.Storage;

// Request/response contracts (API spec §3.1.5).
public record UploadUrlRequest(string FileName, string MimeType, string SensitivityClass,
    string OwnerPolicyId, string? RelatedClaimId, long ExpectedSizeBytes);

public record UploadUrlResponse(string DocumentId, string UploadUrl, string BlobName, string Container,
    DateTimeOffset ExpiresAt, long MaxSizeBytes, string UploadInstructions, string CallbackEvent);

/// <summary>
/// Document metadata (LLD A.3.5 — Cosmos "docs-metadata" container, partition = ownerPolicyId).
/// Lowercase <c>id</c>/<c>ownerPolicyId</c> match Cosmos' id + partition-key conventions.
/// </summary>
public class DocumentMetadata
{
    public string id { get; set; } = default!;
    public string ownerPolicyId { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public string MimeType { get; set; } = default!;
    public string SensitivityClass { get; set; } = default!;
    public string? RelatedClaimId { get; set; }
    public string Container { get; set; } = "docs-staging";
    public string BlobName { get; set; } = default!;
    public string Status { get; set; } = "PendingUpload"; // PendingUpload → Scanned → Promoted → Quarantined
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    // OCR / form-recognition output (Azure Document Intelligence or local stub).
    public string? OcrEngine { get; set; }
    public Dictionary<string, string>? ExtractedFields { get; set; }
}
