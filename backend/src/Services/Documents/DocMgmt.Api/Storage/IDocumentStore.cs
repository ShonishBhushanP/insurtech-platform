using System.Collections.Concurrent;
using InsurTech.BuildingBlocks.Azure;
using Microsoft.Azure.Cosmos;

namespace InsurTech.DocMgmt.Api.Storage;

/// <summary>Document metadata persistence (LLD A.3.5). Cosmos in Azure; in-memory locally.</summary>
public interface IDocumentStore
{
    Task UpsertAsync(DocumentMetadata doc, CancellationToken ct = default);
    Task<DocumentMetadata?> GetAsync(string id, string ownerPolicyId, CancellationToken ct = default);
    Task<DocumentMetadata?> FindAsync(string id, CancellationToken ct = default);
}

/// <summary>In-memory store for local runs.</summary>
public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<string, DocumentMetadata> _docs = new();

    public Task UpsertAsync(DocumentMetadata doc, CancellationToken ct = default)
    {
        _docs[doc.id] = doc;
        return Task.CompletedTask;
    }

    public Task<DocumentMetadata?> GetAsync(string id, string ownerPolicyId, CancellationToken ct = default) =>
        Task.FromResult(_docs.TryGetValue(id, out var d) ? d : null);

    public Task<DocumentMetadata?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_docs.TryGetValue(id, out var d) ? d : null);
}

/// <summary>
/// Azure Cosmos DB store (deployment diagram: Data Tier — Cosmos DB). Container "docs-metadata"
/// partitioned by ownerPolicyId. Activated when <c>Azure:Cosmos:Endpoint</c> is configured;
/// authenticates with Managed Identity (AAD), no keys.
/// </summary>
public sealed class CosmosDocumentStore : IDocumentStore
{
    private readonly Container _container;

    public CosmosDocumentStore(string endpoint, string database, string container)
    {
        var client = new CosmosClient(endpoint, AzureCredential.Instance,
            new CosmosClientOptions { ApplicationName = "insurtech-docmgmt" });
        _container = client.GetContainer(database, container);
    }

    public async Task UpsertAsync(DocumentMetadata doc, CancellationToken ct = default) =>
        await _container.UpsertItemAsync(doc, new PartitionKey(doc.ownerPolicyId), cancellationToken: ct);

    public async Task<DocumentMetadata?> GetAsync(string id, string ownerPolicyId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _container.ReadItemAsync<DocumentMetadata>(id, new PartitionKey(ownerPolicyId), cancellationToken: ct);
            return resp.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<DocumentMetadata?> FindAsync(string id, CancellationToken ct = default)
    {
        // Cross-partition point lookup by id (rare path — retrieval normally carries the policy id).
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id);
        using var iterator = _container.GetItemQueryIterator<DocumentMetadata>(query);
        if (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            return page.FirstOrDefault();
        }
        return null;
    }
}
