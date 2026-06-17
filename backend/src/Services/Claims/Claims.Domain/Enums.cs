namespace InsurTech.Claims.Domain;

/// <summary>
/// Claim lifecycle state machine (LLD Appendix A.1.2). Transitions are enforced in the
/// <see cref="Aggregates.Claim"/> aggregate, never set directly.
/// </summary>
public enum ClaimStatus
{
    Filed,
    Triaged,
    UnderInvestigation,
    ReferredForUnderwriting,
    Approved,
    Rejected,
    Paid,
    Closed,
    Cancelled
}

/// <summary>Claim type — drives the adjudication ruleset (API spec §3.1.1).</summary>
public enum ClaimType { Motor, Health, Property, Travel, Life }

public enum AdjudicationOutcome { Approved, Rejected, Referred }
