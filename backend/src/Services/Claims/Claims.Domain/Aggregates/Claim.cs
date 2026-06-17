using InsurTech.BuildingBlocks.Domain;
using InsurTech.Claims.Domain.Events;
using InsurTech.Claims.Domain.ValueObjects;

namespace InsurTech.Claims.Domain.Aggregates;

/// <summary>
/// Claim aggregate root (LLD Appendix A.1.2 / A.1.5). Enforces the claim lifecycle
/// state machine and raises domain events. All mutation goes through intent methods —
/// <see cref="Status"/> is never set from outside.
/// </summary>
public class Claim : Entity, IAggregateRoot
{
    private readonly List<ClaimStatusEntry> _history = new();
    private readonly List<ClaimDocument> _documents = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string ClaimNumber { get; private set; } = default!;
    public Guid PolicyId { get; private set; }
    public ClaimType Type { get; private set; }
    public ClaimStatus Status { get; private set; }
    public string FiledByUserId { get; private set; } = default!;
    public DateTimeOffset FiledUtc { get; private set; }
    public DateTimeOffset IncidentDate { get; private set; }
    public string IncidentDescription { get; private set; } = default!;
    public string? IncidentAddress { get; private set; }
    public decimal ClaimedAmount { get; private set; }
    public decimal? ApprovedAmount { get; private set; }
    public string CurrencyCode { get; private set; } = "INR";
    public string? DecisionReason { get; private set; }
    public double? FraudScore { get; private set; }
    public string? FraudDecision { get; private set; }
    public string? PaymentReference { get; private set; }

    // Document verification indicator (UI screen #2). Set once the intake pipeline
    // (malware scan + OCR/extraction in Document Mgmt) completes — modelled at triage.
    public bool DocumentsVerified { get; private set; }

    // Optimistic concurrency token (rowversion in SQL MI; guid bump for InMemory).
    public Guid RowVersion { get; private set; } = Guid.NewGuid();

    public IReadOnlyList<ClaimStatusEntry> History => _history.AsReadOnly();
    public IReadOnlyList<ClaimDocument> Documents => _documents.AsReadOnly();

    private Claim() { } // EF

    /// <summary>Factory — files a new claim (FNOL) and raises <see cref="ClaimFiled"/>.</summary>
    public static Claim File(Guid policyId, ClaimType type, string filedByUserId,
        DateTimeOffset incidentDate, string description, string? address, Money claimed,
        IEnumerable<ClaimDocument>? documents = null)
    {
        var claim = new Claim
        {
            PolicyId = policyId,
            Type = type,
            FiledByUserId = filedByUserId,
            FiledUtc = DateTimeOffset.UtcNow,
            IncidentDate = incidentDate,
            IncidentDescription = description,
            IncidentAddress = address,
            ClaimedAmount = claimed.Amount,
            CurrencyCode = claimed.Currency,
            Status = ClaimStatus.Filed,
            ClaimNumber = $"CL-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(100000, 999999)}"
        };
        if (documents is not null) claim._documents.AddRange(documents);
        claim.Record("Claim filed (FNOL received)");
        claim.Raise(new ClaimFiled(claim.Id, claim.ClaimNumber, policyId, claimed.Amount, claimed.Currency));
        return claim;
    }

    /// <summary>Records the fraud triage result. score≥block→UnderInvestigation, else Triaged.</summary>
    public void ApplyTriage(double score, string decision)
    {
        EnsureMutable();
        FraudScore = score;
        FraudDecision = decision;
        DocumentsVerified = true; // intake pipeline (scan + OCR/extraction) completed
        Status = decision.Equals("block", StringComparison.OrdinalIgnoreCase)
            ? ClaimStatus.UnderInvestigation
            : ClaimStatus.Triaged;
        Record($"Fraud triage: score={score:0.00}, decision={decision}");
        Raise(new ClaimTriaged(Id, score, decision));
    }

    public void Approve(Money approved)
    {
        if (Status is not (ClaimStatus.Triaged or ClaimStatus.ReferredForUnderwriting or ClaimStatus.UnderInvestigation))
            throw new InvalidOperationException($"CLM-021: cannot approve a claim in status {Status}.");
        ApprovedAmount = approved.Amount;
        Status = ClaimStatus.Approved;
        DecisionReason = "Adjudication rules passed.";
        Record($"Approved for {approved.Amount:N2} {approved.Currency}");
        Bump();
        Raise(new ClaimApproved(Id, approved.Amount, approved.Currency));
    }

    public void Reject(string reason)
    {
        if (Status is ClaimStatus.Paid or ClaimStatus.Closed or ClaimStatus.Cancelled)
            throw new InvalidOperationException($"CLM-021: cannot reject a claim in status {Status}.");
        Status = ClaimStatus.Rejected;
        DecisionReason = reason;
        Record($"Rejected: {reason}");
        Bump();
        Raise(new ClaimRejected(Id, reason));
    }

    public void ReferForUnderwriting(string reason)
    {
        EnsureMutable();
        Status = ClaimStatus.ReferredForUnderwriting;
        DecisionReason = reason;
        Record($"Referred for underwriting: {reason}");
        Bump();
        Raise(new ClaimReferred(Id, reason));
    }

    public void MarkPaid(string paymentReference)
    {
        if (Status != ClaimStatus.Approved)
            throw new InvalidOperationException($"CLM-021: cannot pay a claim in status {Status}.");
        PaymentReference = paymentReference;
        Status = ClaimStatus.Paid;
        Record($"Settlement captured (ref {paymentReference})");
        Bump();
        Raise(new ClaimPaid(Id, ApprovedAmount ?? ClaimedAmount, CurrencyCode, paymentReference));
    }

    public void Cancel()
    {
        if (Status is ClaimStatus.Paid or ClaimStatus.Closed)
            throw new InvalidOperationException($"CLM-021: cannot cancel a claim in status {Status}.");
        Status = ClaimStatus.Cancelled;
        Record("Cancelled by customer");
        Bump();
        Raise(new ClaimCancelled(Id));
    }

    private void EnsureMutable()
    {
        if (Status is ClaimStatus.Paid or ClaimStatus.Closed or ClaimStatus.Cancelled or ClaimStatus.Rejected)
            throw new InvalidOperationException($"CLM-021: claim in status {Status} is terminal.");
    }

    private void Record(string note) => _history.Add(new ClaimStatusEntry(Status.ToString(), note, DateTimeOffset.UtcNow));
    private void Bump() => RowVersion = Guid.NewGuid();
}

/// <summary>
/// Append-only status history entry — powers the lifecycle tracking UI. Persisted as a JSON
/// column on the Claim (see ClaimsDbContext), not as a separate table.
/// </summary>
public record ClaimStatusEntry(string Status, string Note, DateTimeOffset Timestamp);

/// <summary>A document attached to the claim at FNOL (logical ref into Document Mgmt).</summary>
public record ClaimDocument(string DocumentId, string Type);
