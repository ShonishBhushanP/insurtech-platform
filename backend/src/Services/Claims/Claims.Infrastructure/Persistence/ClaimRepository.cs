using InsurTech.Claims.Application.Abstractions;
using InsurTech.Claims.Domain;
using InsurTech.Claims.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;

namespace InsurTech.Claims.Infrastructure.Persistence;

public sealed class ClaimRepository(ClaimsDbContext db) : IClaimRepository
{
    public void Add(Claim claim) => db.Claims.Add(claim);

    public async Task<Claim?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Claims.FirstOrDefaultAsync(c => c.Id == id, ct); // owned History is auto-included

    public async Task<IReadOnlyList<Claim>> ListAsync(Guid? policyId, string? filedByUserId, ClaimStatus? status, CancellationToken ct = default)
    {
        var query = db.Claims.AsQueryable();
        if (policyId is { } pid) query = query.Where(c => c.PolicyId == pid);
        if (!string.IsNullOrWhiteSpace(filedByUserId)) query = query.Where(c => c.FiledByUserId == filedByUserId);
        if (status is { } st) query = query.Where(c => c.Status == st);
        return await query.OrderByDescending(c => c.FiledUtc).ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
