using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;
using InsurTech.DocMgmt.Api.Ocr;
using InsurTech.DocMgmt.Api.Storage;

// Document Management service (LLD A.3). Issues short-lived pre-signed upload URLs and tracks
// document metadata. With Azure configured it issues real Blob user-delegation SAS and persists
// metadata to Cosmos; locally it uses a loopback PUT + in-memory store. The post-upload pipeline
// (malware scan → OCR → immutable promotion) is represented by the staging→Promoted transition.

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);
builder.Services.AddDocumentStorage(builder.Configuration);
builder.Services.AddDocumentExtraction(builder.Configuration);

var app = builder.Build();
app.UseInsurTechDefaults();

string[] allowedMime = { "image/jpeg", "image/png", "image/webp", "application/pdf",
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document" };
const long MaxSize = 26_214_400; // 25 MB (API spec §3.1.5)

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "documents" }));

// POST /v1/documents/upload-url (API spec §3.1.5 / LLD A.3.3.1)
app.MapPost("/v1/documents/upload-url", async (UploadUrlRequest req, IUploadUrlIssuer issuer, IDocumentStore store, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.FileName) || req.FileName.Length > 255)
        return Error.Validation("DOC-001", "fileName is required and must be <= 255 chars.").ToProblem("documents");
    if (!allowedMime.Contains(req.MimeType))
        return Error.Validation("DOC-001", $"mimeType '{req.MimeType}' is not in the allow-list.").ToProblem("documents");
    if (req.ExpectedSizeBytes is <= 0 or > MaxSize)
        return Error.Validation("DOC-001", "expectedSizeBytes must be > 0 and <= 25 MB.").ToProblem("documents");

    var documentId = $"doc_{Guid.NewGuid():N}"[..16];
    var issued = await issuer.IssueAsync(documentId, req.OwnerPolicyId, req.FileName, req.MimeType, ct);

    await store.UpsertAsync(new DocumentMetadata
    {
        id = documentId, ownerPolicyId = req.OwnerPolicyId, FileName = req.FileName, MimeType = req.MimeType,
        SensitivityClass = req.SensitivityClass, RelatedClaimId = req.RelatedClaimId,
        Container = issued.Container, BlobName = issued.BlobName, Status = "PendingUpload"
    }, ct);

    return Results.Ok(new UploadUrlResponse(documentId, issued.UploadUrl, issued.BlobName, issued.Container,
        issued.ExpiresAt, MaxSize, "PUT the file with header x-ms-blob-type: BlockBlob before the URL expires.",
        "DocumentUploaded"));
}).WithTags("Documents");

// Stub direct-PUT target (local issuer only) — stands in for Blob staging + malware scan +
// OCR/form-recognition (Document Intelligence) + immutable promotion.
app.MapPut("/v1/documents/{id}/_staging-put", async (string id, IDocumentStore store, IDocumentExtraction ocr, CancellationToken ct) =>
{
    var doc = await store.FindAsync(id, ct);
    if (doc is null) return Error.NotFound("DOC-010", "Document not found.").ToProblem("documents");

    // Run OCR / form recognition, then promote.
    doc.ExtractedFields = await ocr.ExtractAsync(doc.FileName, doc.MimeType, doc.SensitivityClass, documentUri: null, ct);
    doc.OcrEngine = ocr.Engine;
    doc.Status = "Promoted";
    await store.UpsertAsync(doc, ct);
    return Results.Ok(new { id, status = "Promoted", ocrEngine = doc.OcrEngine, extractedFields = doc.ExtractedFields,
        note = "malware-scan clean (stub); OCR complete; promoted to docs-immutable" });
}).WithTags("Documents");

// GET /v1/documents/{id}
app.MapGet("/v1/documents/{id}", async (string id, IDocumentStore store, CancellationToken ct) =>
{
    var doc = await store.FindAsync(id, ct);
    return doc is null ? Error.NotFound("DOC-010", "Document not found.").ToProblem("documents") : Results.Ok(doc);
}).WithTags("Documents");

app.Run();
