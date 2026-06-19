import { useEffect, useState } from "react";
import { api } from "../api";
import type { FraudAnalysis, FraudCase } from "../types";
import { Badge, Section, when } from "../ui";

// UI brief screen #4 — Fraud & Risk Alerts (Claims Ops / Adjuster UI).
export default function FraudAlerts() {
  const [cases, setCases] = useState<FraudCase[]>([]);
  const [selected, setSelected] = useState<FraudCase | null>(null);
  const [error, setError] = useState("");
  const [msg, setMsg] = useState("");

  async function load() {
    try { setCases(await api.listFraudCases()); }
    catch (e) { setError(String(e)); }
  }

  useEffect(() => {
    load();
    const t = setInterval(load, 4000);
    return () => clearInterval(t);
  }, []);

  async function decideCase(c: FraudCase, outcome: string) {
    setMsg(""); setError("");
    try {
      await api.decideFraudCase(c.caseId, outcome, `Investigator marked ${outcome}.`);
      // Reflect the decision onto the claim too (confirmed fraud → reject; legit → approve).
      if (outcome === "ConfirmedFraud") await api.decideClaim(c.claimId, "reject", "Confirmed fraudulent by SIU.");
      else await api.decideClaim(c.claimId, "approve");
      setMsg(`Case ${c.caseId} → ${outcome}.`);
      setSelected(null);
      await load();
    } catch (e) { setError(String(e)); }
  }

  const open = cases.filter((c) => c.status === "Open" || c.status === "UnderReview");

  return (
    <Section title="Fraud & Risk Alerts" sub="High-risk and duplicate claims flagged by the AI model for investigation.">
      {error && <div className="alert error">{error}</div>}
      {msg && <div className="alert ok">{msg}</div>}

      <div className="grid cols-3" style={{ marginBottom: 16 }}>
        <div className="card kpi"><span className="v" style={{ color: "#dc2626" }}>{cases.filter(c => c.severity === "High").length}</span><span className="l">High-risk alerts</span></div>
        <div className="card kpi"><span className="v" style={{ color: "#d97706" }}>{cases.filter(c => c.duplicateSuspected).length}</span><span className="l">Duplicate suspected</span></div>
        <div className="card kpi"><span className="v">{open.length}</span><span className="l">Awaiting review</span></div>
      </div>

      <div className="card">
        {cases.length === 0 ? (
          <div className="empty">No fraud alerts. File a high-value or suspicious claim to generate one.</div>
        ) : (
          <table>
            <thead>
              <tr><th>Severity</th><th>Score</th><th>Claim</th><th>Summary</th><th>Flags</th><th>Status</th><th></th></tr>
            </thead>
            <tbody>
              {cases.map((c) => (
                <tr key={c.caseId} className="clickable" onClick={() => setSelected(c)}>
                  <td><Badge value={c.severity} /></td>
                  <td style={{ width: 120 }}>
                    <RiskMeter score={c.initialScore} />
                  </td>
                  <td className="muted">{c.claimId.slice(0, 8)}…</td>
                  <td style={{ maxWidth: 280 }}>{c.claimSummary || <span className="muted">—</span>}</td>
                  <td>{c.duplicateSuspected ? <span className="badge amber">Duplicate</span> : <span className="muted">—</span>}</td>
                  <td><Badge value={c.status} /></td>
                  <td><button className="ghost" onClick={(e) => { e.stopPropagation(); setSelected(c); }}>Review</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {selected && <CaseDrawer c={selected} onClose={() => setSelected(null)} onDecide={decideCase} />}
    </Section>
  );
}

function RiskMeter({ score }: { score: number }) {
  const pct = Math.round(score * 100);
  const color = score >= 0.85 ? "#dc2626" : score >= 0.55 ? "#d97706" : "#16a34a";
  return (
    <div>
      <div className="risk-meter"><div style={{ width: `${pct}%`, background: color }} /></div>
      <span className="muted" style={{ fontSize: 12 }}>{score.toFixed(2)}</span>
    </div>
  );
}

function CaseDrawer({ c, onClose, onDecide }: {
  c: FraudCase; onClose: () => void; onDecide: (c: FraudCase, outcome: string) => void;
}) {
  const maxShap = Math.max(...c.shapTopN.map((s) => Math.abs(s.value)), 0.01);
  const closed = c.status === "ConfirmedFraud" || c.status === "ConfirmedLegit";

  const [analysis, setAnalysis] = useState<FraudAnalysis | null>(null);
  const [analyzing, setAnalyzing] = useState(true);
  useEffect(() => {
    let live = true;
    setAnalyzing(true);
    api.getFraudAnalysis(c.caseId)
      .then((a) => { if (live) setAnalysis(a); })
      .catch(() => { if (live) setAnalysis(null); })
      .finally(() => { if (live) setAnalyzing(false); });
    return () => { live = false; };
  }, [c.caseId]);

  return (
    <div className="card" style={{ borderColor: "#1e40af" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h3 style={{ margin: 0 }}>Investigation — {c.caseId}</h3>
        <button className="ghost" onClick={onClose}>Close</button>
      </div>
      <p className="muted">Opened {when(c.openedUtc)} · model {c.modelVersion} · claim {c.claimId.slice(0, 8)}…</p>

      <div className="grid cols-2">
        <div>
          <label>Risk score</label>
          <div className="score-pill"><Badge value={c.severity} /> &nbsp; {c.initialScore.toFixed(2)}</div>
          <p className="muted" style={{ marginTop: 8 }}>{c.claimSummary}</p>
          {c.duplicateSuspected && <div className="alert amber" style={{ background: "#fef3c7", color: "#92400e" }}>⚠ Duplicate / velocity pattern detected for this policy.</div>}
        </div>
        <div>
          <label>Why the model flagged this (explainability)</label>
          {c.shapTopN.map((s) => (
            <div key={s.feature} style={{ marginBottom: 8 }}>
              <div style={{ display: "flex", justifyContent: "space-between", fontSize: 13 }}>
                <span>{s.feature}</span><span className="muted">{s.value.toFixed(2)}</span>
              </div>
              <div className="shap-bar" style={{ width: `${(Math.abs(s.value) / maxShap) * 100}%` }} />
            </div>
          ))}
        </div>
      </div>

      <div style={{ marginTop: 14 }}>
        <label>AI fraud analysis</label>
        <div className="alert info" style={{ margin: 0 }}>
          {analyzing ? <span className="muted">Generating analysis…</span> : (
            <>
              <div style={{ marginBottom: 6 }}>
                <span className="badge blue">{analysis?.generatedBy ?? "unavailable"}</span>
              </div>
              <div>{analysis?.analysis ?? "No analysis available."}</div>
            </>
          )}
        </div>
      </div>

      {!closed ? (
        <div className="btn-row">
          <button className="danger ghost" onClick={() => onDecide(c, "ConfirmedFraud")}>Confirm fraud → reject claim</button>
          <button className="primary" onClick={() => onDecide(c, "ConfirmedLegit")}>Mark legit → approve claim</button>
        </div>
      ) : (
        <div className="alert info" style={{ marginTop: 12 }}>Case closed: <strong>{c.status}</strong>. {c.decisionReason}</div>
      )}
    </div>
  );
}
