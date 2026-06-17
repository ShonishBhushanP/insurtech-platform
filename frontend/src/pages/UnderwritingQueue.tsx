import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import type { Claim } from "../types";
import { Badge, money, Section, when } from "../ui";

// Adjuster Workbench — Underwriting Queue. Lists claims awaiting a human decision
// (amount-based referrals and SIU holds) and lets the adjuster approve/reject them.
export default function UnderwritingQueue() {
  const [claims, setClaims] = useState<Claim[]>([]);
  const [decided, setDecided] = useState<Claim[]>([]);
  const [error, setError] = useState("");
  const [msg, setMsg] = useState("");
  const [busyId, setBusyId] = useState("");

  async function load() {
    try {
      const [queue, done] = await Promise.all([api.underwritingQueue(), api.decidedClaims()]);
      setClaims(queue);
      setDecided(done);
    } catch (e) { setError(String(e)); }
  }

  useEffect(() => {
    load();
    const t = setInterval(load, 4000);
    return () => clearInterval(t);
  }, []);

  async function decide(c: Claim, outcome: "approve" | "reject") {
    setError(""); setMsg(""); setBusyId(c.claimId);
    try {
      if (outcome === "reject") {
        const reason = window.prompt(`Reason for rejecting ${c.claimNumber}?`, "Outside policy cover.");
        if (reason === null) { setBusyId(""); return; }
        await api.decideClaim(c.claimId, "reject", reason);
      } else {
        await api.decideClaim(c.claimId, "approve", "Underwriter approved — within cover.");
      }
      setMsg(`${c.claimNumber} → ${outcome === "approve" ? "Approved & settled" : "Rejected"}.`);
      await load();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusyId("");
    }
  }

  return (
    <Section title="Underwriting Queue" sub="Claims referred for a human decision — amount over the auto-approve threshold or held for investigation.">
      {error && <div className="alert error">{error}</div>}
      {msg && <div className="alert ok">{msg}</div>}

      <div className="grid cols-3" style={{ marginBottom: 16 }}>
        <div className="card kpi"><span className="v">{claims.length}</span><span className="l">Awaiting decision</span></div>
        <div className="card kpi"><span className="v" style={{ color: "#d97706" }}>{claims.filter(c => c.status === "ReferredForUnderwriting").length}</span><span className="l">Referred (amount)</span></div>
        <div className="card kpi"><span className="v" style={{ color: "#dc2626" }}>{claims.filter(c => c.status === "UnderInvestigation").length}</span><span className="l">Under investigation</span></div>
      </div>

      <div className="card">
        {claims.length === 0 ? (
          <div className="empty">Nothing in the queue. File a claim over ₹1,00,000 to generate a referral.</div>
        ) : (
          <table>
            <thead>
              <tr><th>Claim #</th><th>Type</th><th>Filed</th><th>Amount</th><th>Fraud</th><th>Reason</th><th>Status</th><th></th></tr>
            </thead>
            <tbody>
              {claims.map((c) => (
                <tr key={c.claimId}>
                  <td><Link to={`/claims/${c.claimId}`}>{c.claimNumber}</Link></td>
                  <td>{c.claimType}</td>
                  <td className="muted">{when(c.filedAt)}</td>
                  <td className="amount">{money(c.claimedAmount, c.currency)}</td>
                  <td>{c.fraudScore != null ? `${c.fraudScore.toFixed(2)} / ${c.fraudDecision}` : "—"}</td>
                  <td style={{ maxWidth: 260 }} className="muted">{c.decisionReason ?? "—"}</td>
                  <td><Badge value={c.status} /></td>
                  <td style={{ whiteSpace: "nowrap" }}>
                    <button className="primary" disabled={busyId === c.claimId} onClick={() => decide(c, "approve")} style={{ padding: "7px 12px", marginRight: 6 }}>
                      {busyId === c.claimId ? "…" : "Approve"}
                    </button>
                    <button className="danger ghost" disabled={busyId === c.claimId} onClick={() => decide(c, "reject")}>Reject</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      <p className="muted" style={{ fontSize: 12 }}>Approve settles the claim through Payments (→ Paid). Reject closes it with the captured reason.</p>

      <h2 style={{ fontSize: 18, margin: "26px 0 4px" }}>Recent Decisions</h2>
      <p className="page-sub">Claims that have been approved, settled, or rejected.</p>
      <div className="card">
        {decided.length === 0 ? (
          <div className="empty">No decisions yet.</div>
        ) : (
          <table>
            <thead>
              <tr><th>Claim #</th><th>Type</th><th>Outcome</th><th>Amount</th><th>Fraud</th><th>Decision note</th></tr>
            </thead>
            <tbody>
              {decided.map((c) => (
                <tr key={c.claimId}>
                  <td><Link to={`/claims/${c.claimId}`}>{c.claimNumber}</Link></td>
                  <td>{c.claimType}</td>
                  <td><Badge value={c.status} /></td>
                  <td className="amount">{money(c.approvedAmount ?? c.claimedAmount, c.currency)}</td>
                  <td>{c.fraudScore != null ? `${c.fraudScore.toFixed(2)} / ${c.fraudDecision}` : "—"}</td>
                  <td style={{ maxWidth: 280 }} className="muted">{c.decisionReason ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </Section>
  );
}
