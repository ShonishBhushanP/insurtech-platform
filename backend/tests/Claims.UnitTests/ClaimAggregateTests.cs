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

    [Fact]
    public void ApplyTriage_allow_moves_to_Triaged_and_raises_event()
    {
        var claim = NewFiledClaim();

        claim.ApplyTriage(0.10, "allow");

        claim.Status.Should().Be(ClaimStatus.Triaged);
        claim.FraudDecision.Should().Be("allow");
        claim.DocumentsVerified.Should().BeTrue();
        claim.DomainEvents.Should().ContainSingle(e => e is ClaimTriaged);
    }

    [Fact]
    public void ReferForUnderwriting_sets_status_reason_and_raises_event()
    {
        var claim = NewFiledClaim();
        claim.ApplyTriage(0.70, "refer");

        claim.ReferForUnderwriting("Fraud risk in refer band.");

        claim.Status.Should().Be(ClaimStatus.ReferredForUnderwriting);
        claim.DecisionReason.Should().Contain("refer band");
        claim.DomainEvents.Should().ContainSingle(e => e is ClaimReferred);
    }

    [Fact]
    public void Underwriter_can_Approve_a_referred_claim()
    {
        var claim = NewFiledClaim();
        claim.ApplyTriage(0.70, "refer");
        claim.ReferForUnderwriting("over threshold");

        claim.Approve(Money.Of(45000m));

        claim.Status.Should().Be(ClaimStatus.Approved);
        claim.ApprovedAmount.Should().Be(45000m);
    }

    [Fact]
    public void Reject_from_Triaged_sets_reason_and_raises_event()
    {
        var claim = NewFiledClaim();
        claim.ApplyTriage(0.70, "refer");

        claim.Reject("Policy lapsed at incident date.");

        claim.Status.Should().Be(ClaimStatus.Rejected);
        claim.DecisionReason.Should().Be("Policy lapsed at incident date.");
        claim.DomainEvents.Should().ContainSingle(e => e is ClaimRejected);
    }

    [Fact]
    public void Reject_after_Paid_is_rejected_by_state_machine()
    {
        var claim = NewFiledClaim();
        claim.ApplyTriage(0.10, "allow");
        claim.Approve(Money.Of(45000m));
        claim.MarkPaid("NEFT123");

        var act = () => claim.Reject("too late");

        act.Should().Throw<InvalidOperationException>().WithMessage("*CLM-021*");
    }

    [Fact]
    public void MarkPaid_without_Approve_is_rejected_by_state_machine()
    {
        var claim = NewFiledClaim();
        claim.ApplyTriage(0.10, "allow"); // Triaged, not Approved

        var act = () => claim.MarkPaid("NEFT123");

        act.Should().Throw<InvalidOperationException>().WithMessage("*CLM-021*");
    }

    [Fact]
    public void Cancel_from_Filed_succeeds_and_raises_event()
    {
        var claim = NewFiledClaim();

        claim.Cancel();

        claim.Status.Should().Be(ClaimStatus.Cancelled);
        claim.DomainEvents.Should().ContainSingle(e => e is ClaimCancelled);
    }

    [Fact]
    public void ApplyTriage_on_a_terminal_claim_throws()
    {
        var claim = NewFiledClaim();
        claim.Cancel();

        var act = () => claim.ApplyTriage(0.10, "allow");

        act.Should().Throw<InvalidOperationException>().WithMessage("*CLM-021*");
    }

    [Fact]
    public void File_with_documents_populates_Documents()
    {
        var docs = new[] { new ClaimDocument("doc_1", "PhotoOfDamage") };
        var claim = Claim.File(Guid.NewGuid(), ClaimType.Motor, "usr_1",
            DateTimeOffset.UtcNow.AddDays(-1), "collision", "MG Road", Money.Of(45000m), docs);

        claim.Documents.Should().ContainSingle(d => d.DocumentId == "doc_1" && d.Type == "PhotoOfDamage");
    }

    [Fact]
    public void State_transitions_record_history_and_bump_rowversion()
    {
        var claim = NewFiledClaim();
        var initialVersion = claim.RowVersion;
        var initialHistory = claim.History.Count;

        claim.ApplyTriage(0.10, "allow");
        claim.Approve(Money.Of(45000m));

        claim.RowVersion.Should().NotBe(initialVersion); // Approve bumps the concurrency token
        claim.History.Count.Should().BeGreaterThan(initialHistory); // each transition appends a history entry
    }
}
