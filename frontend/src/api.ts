import type { AuditEvent, CashlessAuthorization, Claim, DocumentMeta, FileClaimResponse, FraudAnalysis, FraudCase, Policy } from "./types";

const BASE = import.meta.env.VITE_API_BASE ?? "http://localhost:8080";

function uuid(): string {
  return crypto.randomUUID();
}

// Requests get a timeout so a cold-starting/stalled service surfaces a clear error
// instead of an infinite spinner (Container Apps scale-to-zero can take ~20-30s to wake).
async function http<T>(path: string, init?: RequestInit, timeoutMs = 75000): Promise<T> {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), timeoutMs);
  let res: Response;
  try {
    res = await fetch(`${BASE}${path}`, {
      ...init,
      signal: ctrl.signal,
      headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) },
    });
  } catch (e) {
    if ((e as Error).name === "AbortError")
      throw new Error("Request timed out — the service may be waking from idle. Please try again.");
    throw e;
  } finally {
    clearTimeout(timer);
  }
  if (!res.ok) {
    let detail = res.statusText;
    try {
      const problem = await res.json();
      detail = problem.detail ?? problem.title ?? detail;
    } catch {
      /* ignore */
    }
    throw new Error(`${res.status} — ${detail}`);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export const api = {
  // ---- Policies ----
  listPolicies: (userId?: string) =>
    http<Policy[]>(`/v1/policies${userId ? `?userId=${userId}` : ""}`),

  createPolicy: (body: unknown) =>
    http<Policy>("/v1/policies", {
      method: "POST",
      headers: { "Idempotency-Key": uuid() },
      body: JSON.stringify(body),
    }),

  // ---- Claims ----
  fileClaim: (body: unknown) =>
    http<FileClaimResponse>("/v1/claims", {
      method: "POST",
      headers: { "Idempotency-Key": uuid() },
      body: JSON.stringify(body),
    }),

  listClaims: (params: { userId?: string; status?: string } = {}) => {
    const qs = new URLSearchParams();
    if (params.userId) qs.set("userId", params.userId);
    if (params.status) qs.set("status", params.status);
    const q = qs.toString();
    return http<Claim[]>(`/v1/claims${q ? `?${q}` : ""}`);
  },

  getClaim: (id: string) => http<Claim>(`/v1/claims/${id}/status`),

  // Underwriting queue: claims awaiting a human decision (amount-based referrals + SIU holds).
  underwritingQueue: async () => {
    const [referred, investigation] = await Promise.all([
      api.listClaims({ status: "ReferredForUnderwriting" }),
      api.listClaims({ status: "UnderInvestigation" }),
    ]);
    return [...referred, ...investigation].sort((a, b) => (a.filedAt < b.filedAt ? 1 : -1));
  },

  // Decided claims (approved / settled / rejected) for the workbench decisions view.
  decidedClaims: async () => {
    const [approved, paid, rejected] = await Promise.all([
      api.listClaims({ status: "Approved" }),
      api.listClaims({ status: "Paid" }),
      api.listClaims({ status: "Rejected" }),
    ]);
    return [...approved, ...paid, ...rejected].sort((a, b) => (a.filedAt < b.filedAt ? 1 : -1));
  },

  cancelClaim: (id: string) => http<Claim>(`/v1/claims/${id}/cancel`, { method: "POST" }),

  decideClaim: (id: string, outcome: "approve" | "reject", reason?: string, approvedAmount?: number) =>
    http<Claim>(`/v1/claims/${id}/decision`, {
      method: "POST",
      body: JSON.stringify({ outcome, reason, approvedAmount }),
    }),

  // ---- Documents ----
  requestUploadUrl: (body: unknown) =>
    http<{ documentId: string; uploadUrl: string; expiresAt: string }>("/v1/documents/upload-url", {
      method: "POST",
      headers: { "Idempotency-Key": uuid() },
      body: JSON.stringify(body),
    }),

  // Upload the file bytes to the documents service (loopback stand-in for direct-to-blob),
  // which stores them and runs the scan + OCR pipeline.
  promoteDocument: async (id: string, file?: File) => {
    const headers: Record<string, string> = {};
    if (file) headers["Content-Type"] = file.type || "application/octet-stream";
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 75000);
    try {
      const res = await fetch(`${BASE}/v1/documents/${id}/_staging-put`, { method: "PUT", headers, body: file, signal: ctrl.signal });
      if (!res.ok) throw new Error(`${res.status} — document upload failed`);
      return res.json();
    } finally {
      clearTimeout(timer);
    }
  },

  getDocument: (id: string) => http<DocumentMeta>(`/v1/documents/${id}`),

  // Direct URL to the stored bytes — used as an <img src> / download link.
  documentContentUrl: (id: string) => `${BASE}/v1/documents/${id}/content`,

  // ---- Fraud ----
  listFraudCases: (status?: string) =>
    http<FraudCase[]>(`/v1/fraud/cases${status ? `?status=${status}` : ""}`),

  getFraudAnalysis: (caseId: string) => http<FraudAnalysis>(`/v1/fraud/cases/${caseId}/analysis`),

  decideFraudCase: (id: string, outcome: string, reason: string) =>
    http<FraudCase>(`/v1/fraud/cases/${id}/decision`, {
      method: "POST",
      body: JSON.stringify({ outcome, reason }),
    }),

  // ---- Partner (cashless authorization) ----
  authorizeCashless: (body: unknown) =>
    http<CashlessAuthorization>("/v1/partner/cashless/authorize", {
      method: "POST",
      headers: { "Idempotency-Key": uuid() },
      body: JSON.stringify(body),
    }),

  // ---- Audit & Compliance ----
  auditQuery: (params: { entity?: string; action?: string } = {}) => {
    const qs = new URLSearchParams();
    if (params.entity) qs.set("entity", params.entity);
    if (params.action) qs.set("action", params.action);
    const q = qs.toString();
    return http<AuditEvent[]>(`/v1/audit/query${q ? `?${q}` : ""}`);
  },
  appendAudit: (body: unknown) =>
    http<AuditEvent>("/v1/audit/events", { method: "POST", body: JSON.stringify(body) }),
  auditVerify: () =>
    http<{ entries: number; verified: number; chainIntact: boolean }>("/v1/audit/verify"),
  generateReport: (type: string) => http<Record<string, unknown>>(`/v1/reports/${type}`),
};
