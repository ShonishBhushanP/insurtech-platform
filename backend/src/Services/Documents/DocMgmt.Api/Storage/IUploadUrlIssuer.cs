using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using InsurTech.BuildingBlocks.Azure;

namespace InsurTech.DocMgmt.Api.Storage;

public record IssuedUpload(string UploadUrl, string BlobName, string Container, DateTimeOffset ExpiresAt);

/// <summary>Issues a short-lived, blob-scoped upload URL for direct browser→storage upload (LLD A.3.3.1).</summary>
public interface IUploadUrlIssuer
{
    bool DirectToBlob { get; }
    Task<IssuedUpload> IssueAsync(string documentId, string ownerPolicyId, string fileName, string mimeType, CancellationToken ct = default);
}

/// <summary>
/// Azure Blob user-delegation SAS issuer (LLD A.3.2 IUploadUrlIssuer). Create+Write only,
/// 15-minute expiry, scoped to a single blob, signed with a user-delegation key (Managed Identity —
/// no account key). Activated when <c>Azure:Storage:BlobServiceUri</c> is configured.
/// </summary>
public sealed class BlobUploadUrlIssuer : IUploadUrlIssuer
{
    private readonly BlobServiceClient _service;
    private readonly string _container;
    private readonly string _accountName;

    public bool DirectToBlob => true;

    public BlobUploadUrlIssuer(string blobServiceUri, string stagingContainer)
    {
        _service = new BlobServiceClient(new Uri(blobServiceUri), AzureCredential.Instance);
        _container = stagingContainer;
        _accountName = new Uri(blobServiceUri).Host.Split('.')[0];
    }

    public async Task<IssuedUpload> IssueAsync(string documentId, string ownerPolicyId, string fileName, string mimeType, CancellationToken ct = default)
    {
        var blobName = $"{ownerPolicyId}/{documentId}/{fileName}";
        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-2);
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(15);

        var udk = await _service.GetUserDelegationKeyAsync(startsOn, expiresOn, ct);

        var sas = new BlobSasBuilder
        {
            BlobContainerName = _container,
            BlobName = blobName,
            Resource = "b",
            StartsOn = startsOn,
            ExpiresOn = expiresOn,
            ContentType = mimeType
        };
        sas.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        var sasToken = sas.ToSasQueryParameters(udk.Value, _accountName).ToString();
        var uploadUrl = $"{_service.Uri}{_container}/{Uri.EscapeDataString(blobName)}?{sasToken}";
        return new IssuedUpload(uploadUrl, blobName, _container, expiresOn);
    }
}

/// <summary>Local stub issuer — a loopback PUT URL standing in for Blob staging (no Azure needed).</summary>
public sealed class LocalUploadUrlIssuer : IUploadUrlIssuer
{
    public bool DirectToBlob => false;

    public Task<IssuedUpload> IssueAsync(string documentId, string ownerPolicyId, string fileName, string mimeType, CancellationToken ct = default)
    {
        var blobName = $"{ownerPolicyId}/{documentId}/{fileName}";
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var uploadUrl = $"http://localhost:5104/v1/documents/{documentId}/_staging-put?sig=stub&se={expiresAt:O}";
        return Task.FromResult(new IssuedUpload(uploadUrl, blobName, "docs-staging", expiresAt));
    }
}
