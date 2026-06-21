import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import { useAuth } from "../auth";
import type { Policy } from "../types";
import { Badge, money, Section, when } from "../ui";

// Policy lifecycle management (LLD A.4). Customer/Agent view of issued policies.
export default function MyPolicies() {
  const { session } = useAuth();
  const [policies, setPolicies] = useState<Policy[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  async function load() {
    setLoading(true);
    try { setPolicies(await api.listPolicies(session?.userId)); }
    catch (e) { setError(String(e)); }
    finally { setLoading(false); }
  }
  useEffect(() => { load(); /* eslint-disable-next-line */ }, []);

  return (
    <Section title="My Policies" sub="Issued policies and their coverage. Onboard a new policy to cover more.">
      {error && <div className="alert error">{error}</div>}
      <div className="btn-row" style={{ marginTop: 0, marginBottom: 16 }}>
        <Link to="/policies/new"><button className="primary">+ New Policy</button></Link>
      </div>
      <div className="card">
        {loading && policies.length === 0 ? <div className="spinner">Loading…</div>
          : policies.length === 0 ? <div className="empty">No policies yet. <Link to="/policies/new">Onboard one →</Link></div>
          : (
            <table>
              <thead><tr><th>Policy #</th><th>Product</th><th>Status</th><th>Sum insured</th><th>Cover until</th><th>Issued</th></tr></thead>
              <tbody>
                {policies.map((p) => (
                  <tr key={p.policyId}>
                    <td>{p.policyNumber}</td>
                    <td className="muted">{p.policyId.slice(0, 8)}…</td>
                    <td><Badge value={p.status} /></td>
                    <td className="amount">{money(p.sumInsured, p.currency)}</td>
                    <td>{p.effectiveTo}</td>
                    <td className="muted">{when(p.issuedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </div>
    </Section>
  );
}
