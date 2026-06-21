import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import { useAuth } from "../auth";
import type { Claim } from "../types";
import { Badge, money, Section, when } from "../ui";

// UI brief screen #3 — Recent Claims Dashboard (customer + agent reuse).
export default function RecentClaims() {
  const { session } = useAuth();
  const [claims, setClaims] = useState<Claim[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  async function load() {
    setLoading(true);
    try { setClaims(await api.listClaims({ userId: session?.userId })); }
    catch (e) { setError(String(e)); }
    finally { setLoading(false); }
  }

  useEffect(() => {
    load();
    const t = setInterval(load, 4000); // poll so async adjudication progress shows live
    return () => clearInterval(t);
  }, []);

  const approved = claims.filter((c) => c.status === "Approved" || c.status === "Paid").length;
  const inProcess = claims.filter((c) => ["Filed", "Triaged", "UnderInvestigation", "ReferredForUnderwriting"].includes(c.status)).length;
  const totalPaid = claims.filter((c) => c.status === "Paid").reduce((s, c) => s + (c.approvedAmount ?? 0), 0);

  return (
    <Section title="Recent Claims" sub="Track every claim you have filed and its live status.">
      {error && <div className="alert error">{error}</div>}

      <div className="grid cols-3" style={{ marginBottom: 16 }}>
        <div className="card kpi"><span className="v">{claims.length}</span><span className="l">Total claims</span></div>
        <div className="card kpi"><span className="v" style={{ color: "#16a34a" }}>{approved}</span><span className="l">Approved / settled</span></div>
        <div className="card kpi"><span className="v" style={{ color: "#d97706" }}>{inProcess}</span><span className="l">In process</span></div>
      </div>

      <div className="card">
        {loading && claims.length === 0 ? (
          <div className="spinner">Loading…</div>
        ) : claims.length === 0 ? (
          <div className="empty">No claims yet. <Link to="/file-claim">File your first claim →</Link></div>
        ) : (
          <table>
            <thead>
              <tr><th>Claim #</th><th>Type</th><th>Filed</th><th>Amount</th><th>Status</th><th>Fraud</th><th></th></tr>
            </thead>
            <tbody>
              {claims.map((c) => (
                <tr key={c.claimId} className="clickable">
                  <td><Link to={`/claims/${c.claimId}`}>{c.claimNumber}</Link></td>
                  <td>{c.claimType}</td>
                  <td className="muted">{when(c.filedAt)}</td>
                  <td className="amount">{money(c.approvedAmount ?? c.claimedAmount, c.currency)}</td>
                  <td><Badge value={c.status} /></td>
                  <td>{c.fraudScore != null ? c.fraudScore.toFixed(2) : "—"}</td>
                  <td><Link to={`/claims/${c.claimId}`}>View →</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      {totalPaid > 0 && <p className="muted">Total settled to date: <strong>{money(totalPaid)}</strong></p>}
    </Section>
  );
}
