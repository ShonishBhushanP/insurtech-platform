namespace InsurTech.DocMgmt.Api.Storage;

public static class DocumentsRegistration
{
    public static IServiceCollection AddDocumentStorage(this IServiceCollection services, IConfiguration config)
    {
        // Metadata store: Cosmos when configured, else in-memory.
        var cosmosEndpoint = config["Azure:Cosmos:Endpoint"];
        if (!string.IsNullOrWhiteSpace(cosmosEndpoint))
        {
            var db = config["Azure:Cosmos:Database"] ?? "insurtech";
            var container = config["Azure:Cosmos:DocumentsContainer"] ?? "docs-metadata";
            services.AddSingleton<IDocumentStore>(_ => new CosmosDocumentStore(cosmosEndpoint, db, container));
        }
        else
        {
            services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
        }

        // Upload-URL issuer: Blob user-delegation SAS when configured, else loopback stub.
        var blobUri = config["Azure:Storage:BlobServiceUri"];
        if (!string.IsNullOrWhiteSpace(blobUri))
        {
            var staging = config["Azure:Storage:StagingContainer"] ?? "docs-staging";
            services.AddSingleton<IUploadUrlIssuer>(_ => new BlobUploadUrlIssuer(blobUri, staging));
        }
        else
        {
            services.AddSingleton<IUploadUrlIssuer, LocalUploadUrlIssuer>();
        }

        return services;
    }
}
