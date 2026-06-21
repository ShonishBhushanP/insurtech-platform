import { useEffect, useState } from "react";
import { api } from "../api";
import type { AuditEvent } from "../types";
import { Section, when } from "../ui";

// Admin / Compliance Portal (LLD A.9) — immutable audit log, tamper-evidence, regulatory reports.
export default function CompliancePortal() {
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [verify, setVerify] = useState<{ entries: number; verified: number; chainIntact: boolean } | null>(null);
  const [report, setReport] = useState<Record<string, unknown> | null>(null);
  const [error, setError] = useState("");

  async function load() {
    try { setEvents(await api.auditQuery()); setVerify(await api.auditVerify()); }
    catch (e) { setError(String(e)); }
  }
  useEffect(() => { load(); /* eslint-disable-next-line */ }, []);

  async function logSample() {
    setError("");
    try {
      await api.appendAudit({ actor: "compliance-officer", action: "AuditReviewed", entity: "Claim", entityId: `clm_${Math.floor(Math.random() * 9000 + 1000)}` });
      await load();
    } catch (e) { setError(String(e)); }
  }

  async function runReport() {
    setError("");
    try { setReport(await api.generateReport("irdai-claims-summary")); }
    catch (e) { setError(String(e)); }
  }

  return (
    <Section title="Audit & Compliance" sub="Immutable, tamper-evident audit trail and regulatory reporting.">
      {error && <div className="alert error">{error}</div>}

      <div className="grid cols-3" style={{ marginBottom: 16 }}>
        <div className="card kpi"><span className="v">{verify?.entries ?? "—"}</span><span className="l">Audit events</span></div>
        <div className="card kpi">
          <span className="v" style={{ color: verify?.chainIntact ? "#16a34a" : "#dc2626" }}>{verify ? (verify.chainIntact ? "✓ Intact" : "✗ Broken") : "—"}</span>
          <span className="l">Hash-chain integrity</span>
        </div>
        <div className="card kpi" style={{ justifyContent: "center" }}>
          <div className="btn-row" style={{ marginTop: 0 }}>
            <button className="primary" onClick={runReport}>Generate IRDAI report</button>
            <button className="ghost" onClick={logSample}>Log sample event</button>
          </div>
        </div>
      </div>

      {report && (
        <div className="card">
          <h3 style={{ marginTop: 0 }}>Regulatory report</h3>
          <pre style={{ background: "var(--surface-2)", padding: 12, borderRadius: 8, overflow: "auto", fontSize: 12 }}>
            {JSON.stringify(report, null, 2)}
          </pre>
        </div>
      )}

      <div className="card">
        <h3 style={{ marginTop: 0 }}>Audit log (append-only, SHA-256 chained)</h3>
        {events.length === 0 ? (
          <div className="empty">No audit events yet. Click "Log sample event", or run the platform to generate them.</div>
        ) : (
          <table>
            <thead><tr><th>Time</th><th>Actor</th><th>Action</th><th>Entity</th><th>Hash</th></tr></thead>
            <tbody>
              {[...events].slice(0, 50).map((e) => (
                <tr key={e.id}>
                  <td className="muted">{when(e.timestamp)}</td>
                  <td>{e.actor}</td>
                  <td><span className="badge blue">{e.action}</span></td>
                  <td className="muted">{e.entity}{e.entityId ? ` · ${e.entityId}` : ""}</td>
                  <td className="muted"><code>{e.hash.slice(0, 12)}…</code></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </Section>
  );
}
