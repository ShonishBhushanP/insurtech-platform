using StackExchange.Redis;

namespace InsurTech.BuildingBlocks.Idempotency;

/// <summary>
/// Azure Cache for Redis-backed idempotency store (API spec §2 — Idempotency-Key → response,
/// 24h TTL). Activated when <c>Azure:Redis:ConnectionString</c> is configured; otherwise the
/// in-memory store is used.
/// </summary>
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private const string KeyPrefix = "idem:";

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(KeyPrefix + key);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task SetAsync(string key, string responseJson, TimeSpan ttl, CancellationToken ct = default)
    {
        await redis.GetDatabase().StringSetAsync(KeyPrefix + key, responseJson, ttl);
    }
}
