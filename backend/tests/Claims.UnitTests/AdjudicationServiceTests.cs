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
    private static Claim FiledClaim(ClaimType type, decimal amount, string desc = "incident") =>
        Claim.File(Guid.NewGuid(), type, "usr_1", DateTimeOffset.UtcNow.AddDays(-1), desc, "MG Road", Money.Of(amount));

    private static AdjudicationService Build(IClaimRepository repo, IFraudScoringClient fraud, IPaymentClient pay, decimal threshold = 100_000m) =>
        new(repo, fraud, pay, new AdjudicationOptions { AutoApproveThreshold = threshold }, NullLogger<AdjudicationService>.Instance);

    [Fact]
    public async Task Low_risk_small_claim_auto_approves_and_settles()
    {
        var claim = FiledClaim(ClaimType.Motor, 45000m, "Minor bumper damage");
        var pay = new FakePayments(captured: true);
        var svc = Build(new FakeRepo(claim), new FakeFraud(0.10, "allow"), pay);

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.Paid);
        claim.PaymentReference.Should().NotBeNull();
        pay.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Refer_band_routes_to_underwriting_without_payment()
    {
        var claim = FiledClaim(ClaimType.Health, 60000m, "Hospitalization");
        var pay = new FakePayments(captured: true);
        var svc = Build(new FakeRepo(claim), new FakeFraud(0.70, "refer"), pay);

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.ReferredForUnderwriting);
        pay.Calls.Should().Be(0);
    }

    [Fact]
    public async Task High_risk_block_diverts_to_investigation_without_payment()
    {
        var claim = FiledClaim(ClaimType.Motor, 90000m, "Vehicle stolen, total loss");
        var pay = new FakePayments(captured: true);
        var svc = Build(new FakeRepo(claim), new FakeFraud(0.92, "block"), pay);

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.UnderInvestigation);
        pay.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Allow_but_amount_over_threshold_routes_to_underwriting()
    {
        // Clean fraud signal, but the amount exceeds the auto-approve ceiling → human underwriting.
        var claim = FiledClaim(ClaimType.Property, 150_000m);
        var pay = new FakePayments(captured: true);
        var svc = Build(new FakeRepo(claim), new FakeFraud(0.10, "allow"), pay, threshold: 100_000m);

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.ReferredForUnderwriting);
        claim.DecisionReason.Should().Contain("threshold");
        pay.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Declined_payment_leaves_claim_Approved_for_compensation()
    {
        // Approve succeeds but the settlement is declined → claim stays Approved (saga compensates), not Paid.
        var claim = FiledClaim(ClaimType.Motor, 45000m);
        var svc = Build(new FakeRepo(claim), new FakeFraud(0.10, "allow"), new FakePayments(captured: false));

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.Approved);
        claim.PaymentReference.Should().BeNull();
    }

    [Fact]
    public async Task Missing_claim_is_a_noop()
    {
        var svc = Build(new NullRepo(), new ThrowingFraud(), new FakePayments(captured: true));

        var act = async () => await svc.RunAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Already_adjudicated_claim_is_skipped_idempotently()
    {
        // A re-run (e.g. duplicate ClaimFiled dispatch) must not re-score or re-pay a settled claim.
        var claim = FiledClaim(ClaimType.Motor, 45000m);
        claim.ApplyTriage(0.10, "allow");
        claim.Approve(Money.Of(45000m));
        claim.MarkPaid("NEFT-PRIOR");
        var pay = new FakePayments(captured: true);
        var svc = Build(new FakeRepo(claim), new ThrowingFraud(), pay); // fraud throws if invoked

        await svc.RunAsync(claim.Id);

        claim.Status.Should().Be(ClaimStatus.Paid);
        claim.PaymentReference.Should().Be("NEFT-PRIOR");
        pay.Calls.Should().Be(0);
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

    private sealed class NullRepo : IClaimRepository
    {
        public void Add(Claim c) { }
        public Task<Claim?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Claim?>(null);
        public Task<IReadOnlyList<Claim>> ListAsync(Guid? p, string? u, ClaimStatus? s, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Claim>>(Array.Empty<Claim>());
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeFraud(double score, string decision) : IFraudScoringClient
    {
        public Task<FraudScoreResult> ScoreAsync(FraudScoreRequest r, CancellationToken ct = default)
            => Task.FromResult(new FraudScoreResult(score, decision, Array.Empty<ShapContribution>(), "test"));
    }

    private sealed class ThrowingFraud : IFraudScoringClient
    {
        public Task<FraudScoreResult> ScoreAsync(FraudScoreRequest r, CancellationToken ct = default)
            => throw new InvalidOperationException("fraud scoring should not be called for a non-Filed claim");
    }

    private sealed class FakePayments(bool captured) : IPaymentClient
    {
        public int Calls { get; private set; }
        public Task<PaymentResult> CaptureAsync(PaymentRequest r, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PaymentResult(captured, "NEFT-TEST", captured ? null : "declined"));
        }
    }
}
