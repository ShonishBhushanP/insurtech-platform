using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;
using InsurTech.Policy.Api.Contracts;
using InsurTech.Policy.Api.Domain;
using InsurTech.Policy.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InsurTech.Policy.Api.Endpoints;

public static class PolicyEndpoints
{
    public static void MapPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/policies").WithTags("Policies");

        // POST /v1/policies — Create New Policy (API spec §3.1.6)
        group.MapPost("/", async (CreatePolicyRequest req, PolicyDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.ProductCode) || req.Coverages is null or { Count: 0 })
                return Error.Validation("POL-001", "productCode and at least one coverage are required.").ToProblem("policy");

            if (req.StartDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
                return Error.Validation("POL-001", "startDate must be today or later.").ToProblem("policy");

            var policy = Domain.Policy.Issue(
                req.ProductCode, req.Policyholder.UserId, req.Policyholder.Name, req.Policyholder.KycRefId,
                req.Coverages.Select(c => new Coverage { Code = c.Code, Limit = c.Limit }),
                req.TenureMonths, req.StartDate,
                req.Premium.Base + req.Premium.Tax,
                req.Coverages.Max(c => c.Limit),
                req.Premium.Currency);

            db.Policies.Add(policy);
            await db.SaveChangesAsync();

            return Results.Created($"/v1/policies/{policy.Id}", ToResponse(policy));
        });

        // GET /v1/policies — list (used by the Customer "File a Claim" policy picker)
        group.MapGet("/", async (PolicyDbContext db, string? userId) =>
        {
            var query = db.Policies.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(userId))
                query = query.Where(p => p.PolicyholderUserId == userId);
            var items = await query.ToListAsync();
            return Results.Ok(items.Select(ToResponse));
        });

        // GET /v1/policies/{id}
        group.MapGet("/{id:guid}", async (Guid id, PolicyDbContext db) =>
        {
            var policy = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            return policy is null
                ? Error.NotFound("POL-010", "Policy not found.").ToProblem("policy")
                : Results.Ok(ToResponse(policy));
        });
    }

    private static PolicyResponse ToResponse(Domain.Policy p) => new(
        p.Id.ToString(), p.PolicyNumber, p.Status.ToString(), p.IssuedUtc,
        p.EffectiveFrom, p.EffectiveTo, p.SumInsured, p.CurrencyCode, p.ETag);
}
