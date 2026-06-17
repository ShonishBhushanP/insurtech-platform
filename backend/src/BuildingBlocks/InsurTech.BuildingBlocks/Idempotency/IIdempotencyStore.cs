namespace InsurTech.BuildingBlocks.Idempotency;

/// <summary>
/// Idempotency-Key store. In Azure this is Redis Enterprise with a 24h TTL
/// (API spec §2 "Idempotency"; LLD A.1.2 IIdempotencyStore). Locally an in-memory
/// implementation is used. Returns a cached response body on replay.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Returns the stored response for a key, or null on first use.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Stores the response body for a key with the configured TTL.</summary>
    Task SetAsync(string key, string responseJson, TimeSpan ttl, CancellationToken ct = default);
}
