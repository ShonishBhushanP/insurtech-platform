import { useState } from "react";
import { api } from "../api";
import type { CashlessAuthorization } from "../types";
import { Badge, money, Section } from "../ui";

// Partner Dashboard (LLD A.7 / API spec §3.1.3) — hospital/garage cashless pre-authorization.
// In production partners reach this over mTLS via a Private Link Service; here it's the same API.
export default function PartnerDashboard() {
  const [partnerId, setPartnerId] = useState("hosp_apollo_blr");
  const [memberId, setMemberId] = useState("MBR-9911772");
  const [policyId, setPolicyId] = useState("pol_a4f9b21c");
  const [treatmentCode, setTreatmentCode] = useState("ICD-10:S82.0");
  const [amount, setAmount] = useState(185000);
  const [facilityType, setFacilityType] = useState("Network-Hospital");
  const [result, setResult] = useState<CashlessAuthorization | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit() {
    setError(""); setResult(null); setBusy(true);
    try {
      setResult(await api.authorizeCashless({
        partnerId, memberId, policyId, treatmentCode,
        estimatedAmount: amount, currency: "INR", facilityType,
        admissionDate: new Date().toISOString(),
      }));
    } catch (e) { setError(String(e)); }
    finally { setBusy(false); }
  }

  return (
    <Section title="Cashless Authorization" sub="Submit a pre-authorization request for a network hospital / garage.">
      {error && <div className="alert error">{error}</div>}
      <div className="grid cols-2">
        <div className="card">
          <label>Partner ID</label>
          <input value={partnerId} onChange={(e) => setPartnerId(e.target.value)} />
          <label>Member ID</label>
          <input value={memberId} onChange={(e) => setMemberId(e.target.value)} />
          <label>Policy ID</label>
          <input value={policyId} onChange={(e) => setPolicyId(e.target.value)} />
          <label>Treatment code (ICD-10)</label>
          <input value={treatmentCode} onChange={(e) => setTreatmentCode(e.target.value)} />
          <label>Estimated amount (INR)</label>
          <input type="number" value={amount} min={1} onChange={(e) => setAmount(Number(e.target.value))} />
          <label>Facility type</label>
          <select value={facilityType} onChange={(e) => setFacilityType(e.target.value)}>
            {["Network-Hospital", "Non-Network-Hospital", "Day-Care", "Emergency"].map((f) => <option key={f}>{f}</option>)}
          </select>
          <div className="btn-row">
            <button className="primary" onClick={submit} disabled={busy}>{busy ? "Authorizing…" : "Request authorization"}</button>
          </div>
        </div>

        <div className="card">
          <h3 style={{ marginTop: 0 }}>Authorization result</h3>
          {!result ? <div className="empty">Submit a request to see the decision.</div> : (
            <>
              <div style={{ marginBottom: 10 }}><Badge value={result.authorizationStatus} /> <span className="muted">({result.responseCode})</span></div>
              <div className="grid cols-2">
                <div className="kpi"><span className="l">Approved amount</span><span className="v" style={{ fontSize: 22 }}>{money(result.approvedAmount, result.currency)}</span></div>
                <div className="kpi"><span className="l">Member co-pay</span><span className="v" style={{ fontSize: 22, color: "#d97706" }}>{money(result.coPayAmount, result.currency)}</span></div>
              </div>
              <p className="muted" style={{ marginTop: 10 }}>{result.responseMessage}</p>
              <table style={{ marginTop: 8 }}>
                <tbody>
                  <tr><td className="muted" style={{ paddingRight: 12 }}>Authorization ID</td><td><code>{result.authorizationId}</code></td></tr>
                  <tr><td className="muted">Reference claim</td><td><code>{result.referenceClaimId}</code></td></tr>
                  <tr><td className="muted">Valid until</td><td>{new Date(result.validUntil).toLocaleString("en-IN")}</td></tr>
                  <tr><td className="muted">Settlement SLA</td><td>{result.settlementSla}</td></tr>
                </tbody>
              </table>
            </>
          )}
        </div>
      </div>
    </Section>
  );
}
