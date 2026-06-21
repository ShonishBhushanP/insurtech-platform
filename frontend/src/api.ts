import type { Claim, DocumentMeta, FileClaimResponse, FraudAnalysis, FraudCase, Policy } from "./types";

const BASE = import.meta.env.VITE_API_BASE ?? "http://localhost:8080";

function uuid(): string {
  return crypto.randomUUID();
}

async function http<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...(init?.headers ?? {}) },
  });
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
    const res = await fetch(`${BASE}/v1/documents/${id}/_staging-put`, { method: "PUT", headers, body: file });
    if (!res.ok) throw new Error(`${res.status} — document upload failed`);
    return res.json();
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
};
