using InsurTech.BuildingBlocks.Domain;

namespace InsurTech.Claims.Domain.Events;

// Domain events raised by the Claim aggregate. The infrastructure layer turns these into
// outbox rows (transactional outbox — LLD A.1.8) which are dispatched as integration events.

public record ClaimFiled(Guid ClaimId, string ClaimNumber, Guid PolicyId, decimal ClaimedAmount, string Currency)
    : IDomainEvent { public string EventType => "ClaimFiled"; }

public record ClaimTriaged(Guid ClaimId, double FraudScore, string FraudDecision)
    : IDomainEvent { public string EventType => "ClaimTriaged"; }

public record ClaimApproved(Guid ClaimId, decimal ApprovedAmount, string Currency)
    : IDomainEvent { public string EventType => "ClaimApproved"; }

public record ClaimRejected(Guid ClaimId, string Reason)
    : IDomainEvent { public string EventType => "ClaimRejected"; }

public record ClaimReferred(Guid ClaimId, string Reason)
    : IDomainEvent { public string EventType => "ClaimReferred"; }

public record ClaimPaid(Guid ClaimId, decimal Amount, string Currency, string PaymentReference)
    : IDomainEvent { public string EventType => "ClaimPaid"; }

public record ClaimCancelled(Guid ClaimId)
    : IDomainEvent { public string EventType => "ClaimCancelled"; }
