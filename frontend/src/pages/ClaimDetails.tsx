import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useParams, Link } from "react-router-dom";
import { api } from "../api";
import type { Claim, DocumentMeta } from "../types";
import { Badge, LifecycleStepper, money, Section, when } from "../ui";

// UI brief screen #2 — Claim Details & Lifecycle Tracking.
// Covers: Claim ID · type · filed date · current status · estimated amount ·
// fraud & document verification indicators · visual claim state progression.
export default function ClaimDetails() {
  const { id = "" } = useParams();
  const [claim, setClaim] = useState<Claim | null>(null);
  const [docMeta, setDocMeta] = useState<Record<string, DocumentMeta>>({});
  const [preview, setPreview] = useState<string | null>(null); // documentId being previewed
  const [error, setError] = useState("");

  const load = useCallback(async () => {
    try { setClaim(await api.getClaim(id)); }
    catch (e) { setError(String(e)); }
  }, [id]);

  useEffect(() => {
    load();
    const t = setInterval(load, 3000); // live progression while the saga runs
    return () => clearInterval(t);
  }, [load]);

  // Fetch OCR / extracted fields for each attached document (once per doc set).
  const docIdsKey = (claim?.documents ?? []).map((d) => d.documentId).join(",");
  useEffect(() => {
    (claim?.documents ?? []).forEach(async (d) => {
      try {
        const m = await api.getDocument(d.documentId);
        setDocMeta((prev) => ({ ...prev, [d.documentId]: m }));
      } catch { /* doc may not be promoted yet */ }
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [docIdsKey]);

  if (error) return <div className="alert error">{error}</div>;
  if (!claim) return <div className="spinner">Loading claim…</div>;

  const fraudTone = claim.fraudScore == null ? "gray" : claim.fraudScore >= 0.85 ? "red" : claim.fraudScore >= 0.55 ? "amber" : "green";
  const docCount = claim.documents?.length ?? 0;

  return (
    <Section title={`Claim ${claim.claimNumber}`} sub={`${claim.claimType} claim · filed ${when(claim.filedAt)}`}>
      <Link to="/claims" className="muted">← Back to claims</Link>

      {/* Header: status + estimated amount + visual progression */}
      <div className="card" style={{ marginTop: 12 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 12 }}>
          <div><Badge value={claim.status} /></div>
          <div className="amount" style={{ fontSize: 20 }}>
            {money(claim.approvedAmount ?? claim.claimedAmount, claim.currency)}
            {claim.approvedAmount != null && claim.approvedAmount !== claim.claimedAmount &&
              <span className="muted" style={{ fontSize: 13 }}> (claimed {money(claim.claimedAmount, claim.currency)})</span>}
          </div>
        </div>
        <LifecycleStepper status={claim.status} />
      </div>

      {/* Claim ID · type · filed/incident dates */}
      <div className="card">
        <h3 style={{ marginTop: 0 }}>Claim details</h3>
        <div className="grid cols-3">
          <Field label="Claim ID"><code>{claim.claimId}</code></Field>
          <Field label="Claim number">{claim.claimNumber}</Field>
          <Field label="Claim type">{claim.claimType}</Field>
          <Field label="Filed date">{when(claim.filedAt)}</Field>
          <Field label="Incident date">{when(claim.incidentDate)}</Field>
          <Field label="Policy">{claim.policyId.slice(0, 8)}…</Field>
        </div>
      </div>

      {/* Fraud & document verification indicators */}
      <div className="grid cols-2">
        <div className="card">
          <h3 style={{ marginTop: 0 }}>Verification &amp; Risk</h3>
          <div className="grid cols-2">
            <div className="kpi">
              <span className="l">Fraud check</span>
              <span className="score-pill">
                <Badge value={claim.fraudDecision ?? "pending"} />&nbsp;
                <span className={`badge ${fraudTone}`}>{claim.fraudScore != null ? claim.fraudScore.toFixed(2) : "—"}</span>
              </span>
              <span className="muted" style={{ fontSize: 12 }}>
                {claim.fraudScore == null ? "Awaiting fraud scoring"
                  : claim.fraudScore >= 0.85 ? "High risk — diverted to investigation"
                  : claim.fraudScore >= 0.55 ? "Elevated risk — referred for review"
                  : "Low risk — cleared"}
              </span>
            </div>
            <div className="kpi">
              <span className="l">Document verification</span>
              <span>
                {docCount === 0
                  ? <span className="badge gray">No documents attached</span>
                  : claim.documentsVerified
                    ? <span className="badge green">✓ {docCount} verified</span>
                    : <span className="badge amber">{docCount} pending scan</span>}
              </span>
              <span className="muted" style={{ fontSize: 12 }}>
                {docCount === 0 ? "FNOL filed without attachments"
                  : claim.documentsVerified ? "Malware-scanned &amp; extracted (Document Mgmt)"
                  : "Awaiting intake pipeline"}
              </span>
            </div>
          </div>

          {claim.decisionReason && <p className="muted" style={{ marginTop: 12 }}>Decision note: {claim.decisionReason}</p>}
        </div>

        <div className="card">
          <h3 style={{ marginTop: 0 }}>Estimated settlement</h3>
          <div className="kpi">
            <span className="v">{money(claim.approvedAmount ?? claim.claimedAmount, claim.currency)}</span>
            <span className="l">{claim.status === "Paid" ? "Settled to your account" : "Estimated claim amount"}</span>
          </div>
          {claim.status === "Paid" && <p className="muted" style={{ marginTop: 12 }}>Payment captured — see lifecycle below.</p>}
        </div>
      </div>

      {/* Documents & extracted data (Document Intelligence OCR output) */}
      <div className="card">
        <h3 style={{ marginTop: 0 }}>Documents &amp; extracted data</h3>
        {docCount === 0 ? (
          <div className="empty">No documents attached to this claim.</div>
        ) : (
          <div className="grid cols-2">
            {claim.documents.map((d) => {
              const meta = docMeta[d.documentId];
              const fields = meta?.extractedFields
                ? Object.entries(meta.extractedFields).filter(([k]) => !k.startsWith("_"))
                : [];
              const isImage = (meta?.mimeType ?? "").startsWith("image/");
              const typeVerified = meta?.extractedFields?.["documentTypeVerified"];
              return (
                <div key={d.documentId} className="card" style={{ margin: 0, background: "var(--surface-2)" }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                    {isImage ? (
                      <img src={api.documentContentUrl(d.documentId)} alt={meta?.fileName}
                        onClick={() => setPreview(d.documentId)}
                        style={{ width: 48, height: 48, objectFit: "cover", borderRadius: 8, cursor: "pointer", border: "1px solid var(--line)" }} />
                    ) : (
                      <div style={{ fontSize: 28 }}>📄</div>
                    )}
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div style={{ fontWeight: 600, overflow: "hidden", textOverflow: "ellipsis" }}>{meta?.fileName ?? d.type}</div>
                      <div className="muted" style={{ fontSize: 12 }}>{meta?.mimeType ?? "—"} · {d.type}</div>
                      {isImage && <a style={{ fontSize: 12, cursor: "pointer" }} onClick={() => setPreview(d.documentId)}>Click to preview</a>}
                    </div>
                    {d.verified ? <span className="badge green">verified</span> : <span className="badge amber">pending</span>}
                  </div>
                  {typeVerified && (
                    <div style={{ marginTop: 6 }}>
                      <span className={`badge ${typeVerified.startsWith("true") ? "green" : "amber"}`}>
                        type {typeVerified.startsWith("true") ? "verified ✓" : "unverified"}
                      </span>
                    </div>
                  )}
                  {meta?.ocrEngine && <div style={{ marginTop: 8 }}><span className="badge blue">OCR · {meta.ocrEngine}</span></div>}
                  {fields.length > 0 ? (
                    <table style={{ marginTop: 8 }}>
                      <tbody>
                        {fields.map(([k, v]) => (
                          <tr key={k}>
                            <td style={{ padding: "3px 10px 3px 0", color: "#64748b", whiteSpace: "nowrap", verticalAlign: "top" }}>{k}</td>
                            <td style={{ padding: "3px 0" }}>{v}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  ) : (
                    <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>Awaiting OCR / form recognition…</p>
                  )}
                  <div className="muted" style={{ fontSize: 11, marginTop: 8 }}><code>{d.documentId}</code></div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Lifecycle history */}
      <div className="card">
        <h3 style={{ marginTop: 0 }}>Lifecycle history</h3>
        <ul className="timeline">
          {[...claim.history].reverse().map((h, i) => (
            <li key={i}>
              <div className="t-status">{h.status}</div>
              <div className="t-note">{h.note}</div>
              <div className="t-time">{when(h.timestamp)}</div>
            </li>
          ))}
        </ul>
      </div>

      {/* Image preview lightbox */}
      {preview && (
        <div onClick={() => setPreview(null)}
          style={{ position: "fixed", inset: 0, background: "rgba(15,23,42,0.8)", display: "grid", placeItems: "center", zIndex: 50, padding: 24 }}>
          <div style={{ textAlign: "center" }}>
            <img src={api.documentContentUrl(preview)} alt="document"
              style={{ maxWidth: "90vw", maxHeight: "85vh", borderRadius: 10, boxShadow: "0 8px 40px rgba(0,0,0,.5)" }} />
            <div style={{ color: "#fff", marginTop: 10, fontSize: 13 }}>{docMeta[preview]?.fileName} — click anywhere to close</div>
          </div>
        </div>
      )}
    </Section>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="kpi">
      <span className="l">{label}</span>
      <span style={{ fontSize: 14, wordBreak: "break-all" }}>{children}</span>
    </div>
  );
}
