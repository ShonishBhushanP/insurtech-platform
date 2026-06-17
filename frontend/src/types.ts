// Wire types mirroring the .NET service responses (API spec §3 / LLD Appendix A).

export interface Policy {
  policyId: string;
  policyNumber: string;
  status: string;
  issuedAt: string;
  effectiveFrom: string;
  effectiveTo: string;
  sumInsured: number;
  currency: string;
  etag: string;
}

export interface ClaimStatusEntry {
  status: string;
  note: string;
  timestamp: string;
}

export interface ClaimDocument {
  documentId: string;
  type: string;
  verified: boolean;
}

export interface Claim {
  claimId: string;
  claimNumber: string;
  policyId: string;
  claimType: string;
  status: string;
  claimedAmount: number;
  approvedAmount: number | null;
  currency: string;
  fraudScore: number | null;
  fraudDecision: string | null;
  documentsVerified: boolean;
  decisionReason: string | null;
  filedAt: string;
  incidentDate: string;
  documents: ClaimDocument[];
  history: ClaimStatusEntry[];
}

export interface FileClaimResponse {
  claimId: string;
  claimNumber: string;
  status: string;
  statusUrl: string;
  filedAt: string;
}

export interface ShapContribution {
  feature: string;
  value: number;
}

export interface FraudCase {
  caseId: string;
  claimId: string;
  policyId: string;
  status: string;
  severity: string;
  initialScore: number;
  duplicateSuspected: boolean;
  modelVersion: string;
  shapTopN: ShapContribution[];
  claimSummary: string;
  decisionReason: string | null;
  openedUtc: string;
}
