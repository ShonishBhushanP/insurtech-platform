using FluentAssertions;
using InsurTech.Claims.Domain;
using InsurTech.Claims.Domain.Aggregates;
using InsurTech.Claims.Domain.Events;
using InsurTech.Claims.Domain.ValueObjects;
using Xunit;

namespace InsurTech.Claims.UnitTests;

public class ClaimAggregateTests
{
    private static Claim NewFiledClaim() => Claim.File(
        Guid.NewGuid(), ClaimType.Motor, "usr_1",
        DateTimeOffset.UtcNow.AddDays(-1), "Rear-end collision", "MG Road", Money.Of(45000m));

    [Fact]
    public void File_sets_status_and_raises_ClaimFiled()
    {
        var claim = NewFiledClaim();

        claim.Status.Should().Be(ClaimStatus.Filed);
        claim.ClaimNumber.Should().StartWith("CL-");
        claim.DomainEvents.Should().ContainSingle(e => e is ClaimFiled);
        claim.History.Should().ContainSingle();
    }

    [Fact]
    public void ApplyTriage_block_moves_to_UnderInvestigation()
    {
        var claim = NewFiledClaim();

        claim.ApplyTriage(0.91, "block");

        claim.Status.Should().Be(ClaimStatus.UnderInvestigation);
        claim.FraudScore.Should().Be(0.91);
    }

    [Fact]
    public void ApplyTriage_allow_then_Approve_then_MarkPaid_walks_to_Paid()
    {
        var claim = NewFiledClaim();

        claim.ApplyTriage(0.10, "allow");
        claim.Approve(Money.Of(45000m));
        claim.MarkPaid("NEFT123");

        claim.Status.Should().Be(ClaimStatus.Paid);
        claim.ApprovedAmount.Should().Be(45000m);
        claim.PaymentReference.Should().Be("NEFT123");
    }

    [Fact]
    public void Approve_from_Filed_is_rejected_by_state_machine()
    {
        var claim = NewFiledClaim();

        var act = () => claim.Approve(Money.Of(45000m));

        act.Should().Throw<InvalidOperationException>().WithMessage("*CLM-021*");
    }

    [Fact]
    public void Cancel_after_Paid_is_rejected()
    {
        var claim = NewFiledClaim();
        claim.ApplyTriage(0.10, "allow");
        claim.Approve(Money.Of(45000m));
        claim.MarkPaid("NEFT123");

        var act = () => claim.Cancel();

        act.Should().Throw<InvalidOperationException>().WithMessage("*CLM-021*");
    }

    [Fact]
    public void Money_rejects_invalid_currency()
    {
        var act = () => Money.Of(100m, "RUPEE");
        act.Should().Throw<ArgumentException>();
    }
}
