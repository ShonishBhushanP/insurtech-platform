using System.Collections.Concurrent;

namespace InsurTech.BuildingBlocks.Idempotency;

/// <summary>In-memory <see cref="IIdempotencyStore"/> for local runs (Redis stands in for prod).</summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (string Response, DateTimeOffset Expiry)> _store = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.Expiry > DateTimeOffset.UtcNow)
                return Task.FromResult<string?>(entry.Response);
            _store.TryRemove(key, out _);
        }
        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(string key, string responseJson, TimeSpan ttl, CancellationToken ct = default)
    {
        _store[key] = (responseJson, DateTimeOffset.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }
}
