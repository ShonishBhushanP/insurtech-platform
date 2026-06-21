import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api";
import { useAuth } from "../auth";
import { money, Section } from "../ui";

// Policy onboarding / issuance (LLD A.4 / sequence 10c). Calls POST /v1/policies.
const PRODUCTS = [
  { code: "MOTOR-COMPREHENSIVE-V3", label: "Motor — Comprehensive", coverage: "OD", ratePerMille: 0.0198 },
  { code: "HEALTH-FAMILY-FLOATER-V2", label: "Health — Family Floater", coverage: "HOSP", ratePerMille: 0.0284 },
  { code: "PROPERTY-HOME-V1", label: "Property — Home", coverage: "FIRE", ratePerMille: 0.0125 },
];

export default function NewPolicy() {
  const nav = useNavigate();
  const { session } = useAuth();
  const [productCode, setProductCode] = useState(PRODUCTS[0].code);
  const [holderName, setHolderName] = useState(session?.role === "agent" ? "" : "R. Sharma");
  const [sumInsured, setSumInsured] = useState(1_100_000);
  const [tenureMonths, setTenureMonths] = useState(12);
  const [startDate, setStartDate] = useState(new Date().toISOString().slice(0, 10));
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const product = PRODUCTS.find((p) => p.code === productCode)!;
  const base = Math.round(sumInsured * product.ratePerMille);
  const tax = Math.round(base * 0.18); // 18% GST
  const total = base + tax;

  async function submit() {
    setError(""); setBusy(true);
    try {
      const policy = await api.createPolicy({
        productCode,
        policyholder: { userId: session?.userId ?? "usr_8b2", name: holderName || "New Customer", kycRefId: "kyc_demo" },
        insuredItem: { make: "—", model: "—" },
        coverages: [{ code: product.coverage, limit: sumInsured }],
        tenureMonths,
        startDate,
        premium: { base, tax, currency: "INR" },
      });
      nav("/policies", { state: { issued: policy.policyNumber } });
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Section title="Onboard a New Policy" sub={session?.role === "agent" ? "Issue a policy on behalf of a customer." : "Buy a new policy."}>
      {error && <div className="alert error">{error}</div>}
      <div className="card" style={{ maxWidth: 640 }}>
        <label>Product</label>
        <select value={productCode} onChange={(e) => setProductCode(e.target.value)}>
          {PRODUCTS.map((p) => <option key={p.code} value={p.code}>{p.label}</option>)}
        </select>

        <label>Policyholder name</label>
        <input value={holderName} placeholder="Customer name" onChange={(e) => setHolderName(e.target.value)} />

        <label>Sum insured (INR)</label>
        <input type="number" value={sumInsured} min={50000} step={50000} onChange={(e) => setSumInsured(Number(e.target.value))} />

        <label>Tenure</label>
        <select value={tenureMonths} onChange={(e) => setTenureMonths(Number(e.target.value))}>
          {[6, 12, 24, 36].map((m) => <option key={m} value={m}>{m} months</option>)}
        </select>

        <label>Cover start date</label>
        <input type="date" value={startDate} min={new Date().toISOString().slice(0, 10)} onChange={(e) => setStartDate(e.target.value)} />

        <div className="card" style={{ background: "var(--surface-2)", marginTop: 14 }}>
          <div style={{ display: "flex", justifyContent: "space-between" }}><span className="muted">Base premium</span><span className="amount">{money(base)}</span></div>
          <div style={{ display: "flex", justifyContent: "space-between" }}><span className="muted">GST (18%)</span><span className="amount">{money(tax)}</span></div>
          <div style={{ display: "flex", justifyContent: "space-between", fontWeight: 700, marginTop: 6 }}><span>Total premium</span><span className="amount">{money(total)}</span></div>
        </div>

        <div className="btn-row">
          <button className="primary" onClick={submit} disabled={busy}>
            {busy ? "Issuing… (first request can take ~20s if the service is waking)" : "Issue policy"}
          </button>
        </div>
        <p className="muted" style={{ fontSize: 12, marginTop: 12 }}>
          Underwriting re-computes the tariff server-side and rejects if the quote drifts &gt; 1% (API spec §3.1.6).
        </p>
      </div>
    </Section>
  );
}
