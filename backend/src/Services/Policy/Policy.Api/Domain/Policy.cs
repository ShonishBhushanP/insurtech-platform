using InsurTech.BuildingBlocks.Domain;

namespace InsurTech.Policy.Api.Domain;

public enum PolicyStatus { Issued, PendingPayment, Cancelled, Lapsed, Renewed }

/// <summary>
/// Policy aggregate (LLD Appendix A.4). Owns coverage limits and validity window;
/// referenced by Claims only via logical id (no FK across service boundaries).
/// </summary>
public class Policy : Entity, IAggregateRoot
{
    private readonly List<Coverage> _coverages = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string PolicyNumber { get; private set; } = default!;
    public string ProductCode { get; private set; } = default!;
    public string PolicyholderUserId { get; private set; } = default!;
    public string PolicyholderName { get; private set; } = default!;
    public string? KycRefId { get; private set; }
    public PolicyStatus Status { get; private set; }
    public string CurrencyCode { get; private set; } = "INR";
    public decimal PremiumTotal { get; private set; }
    public decimal SumInsured { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly EffectiveTo { get; private set; }
    public DateTimeOffset IssuedUtc { get; private set; }
    public string ETag { get; private set; } = Guid.NewGuid().ToString("N");

    public IReadOnlyCollection<Coverage> Coverages => _coverages.AsReadOnly();

    private Policy() { } // EF

    public static Policy Issue(
        string productCode, string userId, string name, string? kycRefId,
        IEnumerable<Coverage> coverages, int tenureMonths, DateOnly startDate,
        decimal premiumTotal, decimal sumInsured, string currency)
    {
        var policy = new Policy
        {
            ProductCode = productCode,
            PolicyholderUserId = userId,
            PolicyholderName = name,
            KycRefId = kycRefId,
            Status = string.IsNullOrWhiteSpace(kycRefId) ? PolicyStatus.PendingPayment : PolicyStatus.Issued,
            CurrencyCode = currency,
            PremiumTotal = premiumTotal,
            SumInsured = sumInsured,
            EffectiveFrom = startDate,
            EffectiveTo = startDate.AddMonths(tenureMonths),
            IssuedUtc = DateTimeOffset.UtcNow,
            PolicyNumber = $"PL-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(100000, 999999)}"
        };
        policy._coverages.AddRange(coverages);
        return policy;
    }
}

public class Coverage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = default!;
    public decimal Limit { get; set; }
}
