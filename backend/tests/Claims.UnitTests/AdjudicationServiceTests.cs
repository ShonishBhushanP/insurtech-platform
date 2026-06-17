using FluentAssertions;
using InsurTech.Claims.Application.Abstractions;
using InsurTech.Claims.Application.Adjudication;
using InsurTech.Claims.Domain;
using InsurTech.Claims.Domain.Aggregates;
using InsurTech.Claims.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InsurTech.Claims.UnitTests;

public class AdjudicationServiceTests
{
    [Fact]
    public async Task Low_risk_small_claim_auto_approves_and_settles()
    {
        var claim = Claim.File(Guid.NewGuid(), ClaimType.Motor, "usr_1",
            DateTimeOffset.UtcNow.AddDays(-1), "Minor bumper damage", "MG Road", Money.Of(45000m));
        var repo = new FakeRepo(claim);
        var svc = new AdjudicationService(repo,
            new FakeFraud(0.10, "allow"),
            new FakePayments(captured: true),
            new AdjudicationOptions { AutoApproveThreshold = 100_000m },
            NullLogger<AdjudicationService>.Instance);

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.Paid);
    }

    [Fact]
    public async Task Refer_band_routes_to_underwriting()
    {
        var claim = Claim.File(Guid.NewGuid(), ClaimType.Health, "usr_1",
            DateTimeOffset.UtcNow.AddDays(-1), "Hospitalization", null, Money.Of(60000m));
        var repo = new FakeRepo(claim);
        var svc = new AdjudicationService(repo,
            new FakeFraud(0.70, "refer"),
            new FakePayments(captured: true),
            new AdjudicationOptions(),
            NullLogger<AdjudicationService>.Instance);

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.ReferredForUnderwriting);
    }

    [Fact]
    public async Task High_risk_block_diverts_to_investigation()
    {
        var claim = Claim.File(Guid.NewGuid(), ClaimType.Motor, "usr_1",
            DateTimeOffset.UtcNow.AddDays(-1), "Vehicle stolen, total loss", null, Money.Of(90000m));
        var repo = new FakeRepo(claim);
        var svc = new AdjudicationService(repo,
            new FakeFraud(0.92, "block"),
            new FakePayments(captured: true),
            new AdjudicationOptions(),
            NullLogger<AdjudicationService>.Instance);

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.UnderInvestigation);
    }

    // ---- test doubles ----
    private sealed class FakeRepo(Claim claim) : IClaimRepository
    {
        public void Add(Claim c) { }
        public Task<Claim?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Claim?>(claim);
        public Task<IReadOnlyList<Claim>> ListAsync(Guid? p, string? u, ClaimStatus? s, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Claim>>(new[] { claim });
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeFraud(double score, string decision) : IFraudScoringClient
    {
        public Task<FraudScoreResult> ScoreAsync(FraudScoreRequest r, CancellationToken ct = default)
            => Task.FromResult(new FraudScoreResult(score, decision, Array.Empty<ShapContribution>(), "test"));
    }

    private sealed class FakePayments(bool captured) : IPaymentClient
    {
        public Task<PaymentResult> CaptureAsync(PaymentRequest r, CancellationToken ct = default)
            => Task.FromResult(new PaymentResult(captured, "NEFT-TEST", captured ? null : "declined"));
    }
}
