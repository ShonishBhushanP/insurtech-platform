import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api";
import { useAuth } from "../auth";
import type { Policy } from "../types";
import { money, Section } from "../ui";

// Label an attachment by the claim type so downstream OCR/adjuster context is meaningful
// (a Life claim image is a death certificate, not a photo of vehicle damage).
function attachmentType(claimType: string, file: File): string {
  if (!file.type.startsWith("image/")) return "ClaimDocument";
  switch (claimType) {
    case "Motor":
    case "Property": return "PhotoOfDamage";
    case "Life": return "DeathCertificate";
    case "Health": return "MedicalDocument";
    default: return "SupportingPhoto";
  }
}

// UI brief screen #1 — File a Claim (Policy Holder / Agent UI).
export default function FileClaim() {
  const nav = useNavigate();
  const { session } = useAuth();
  const userId = session?.userId ?? "usr_8b2";
  const [policies, setPolicies] = useState<Policy[]>([]);
  const [policyId, setPolicyId] = useState("");
  const [claimType, setClaimType] = useState("Motor");
  const [amount, setAmount] = useState(45000);
  const [incidentDate, setIncidentDate] = useState(new Date().toISOString().slice(0, 10));
  const [description, setDescription] = useState("Rear-end collision at signal; minor rear bumper damage.");
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.listPolicies(userId)
      .then((p) => { setPolicies(p); if (p[0]) setPolicyId(p[0].policyId); })
      .catch((e) => setError(String(e)));
  }, []);

  async function submit() {
    setError(""); setBusy(true);
    try {
      // Optional document upload (direct-to-blob SAS pattern — LLD A.3.3.1).
      const attachments: { documentId: string; type: string }[] = [];
      if (file) {
        const doc = await api.requestUploadUrl({
          fileName: file.name,
          mimeType: file.type || "application/octet-stream",
          sensitivityClass: "PII-Image",
          ownerPolicyId: policyId, relatedClaimId: null, expectedSizeBytes: file.size,
        });
        // Upload the bytes → scan + OCR (Document Intelligence) + promote pipeline.
        await api.promoteDocument(doc.documentId, file);
        attachments.push({ documentId: doc.documentId, type: attachmentType(claimType, file) });
      }

      const res = await api.fileClaim({
        policyId, claimType,
        incidentDate: new Date(incidentDate).toISOString(),
        incidentLocation: { lat: 12.9716, lng: 77.5946, address: "MG Road, Bengaluru" },
        description, estimatedAmount: amount, currency: "INR",
        attachments,
        filedBy: { userId, channel: session?.channel ?? "WebMFE" },
      });
      nav(`/claims/${res.claimId}`);
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Section title="File a Claim" sub="Submit a first notice of loss (FNOL). Adjudication starts automatically.">
      {error && <div className="alert error">{error}</div>}
      <div className="card" style={{ maxWidth: 640 }}>
        <label>Policy</label>
        <select value={policyId} onChange={(e) => setPolicyId(e.target.value)}>
          {policies.map((p) => (
            <option key={p.policyId} value={p.policyId}>
              {p.policyNumber} — cover {money(p.sumInsured)} (valid to {p.effectiveTo})
            </option>
          ))}
        </select>

        <label>Claim type</label>
        <select value={claimType} onChange={(e) => setClaimType(e.target.value)}>
          {["Motor", "Health", "Property", "Travel", "Life"].map((t) => <option key={t}>{t}</option>)}
        </select>

        <label>Incident date</label>
        <input type="date" value={incidentDate} max={new Date().toISOString().slice(0, 10)}
          onChange={(e) => setIncidentDate(e.target.value)} />

        <label>Estimated amount (INR)</label>
        <input type="number" value={amount} min={1} onChange={(e) => setAmount(Number(e.target.value))} />

        <label>What happened?</label>
        <textarea value={description} maxLength={2000} onChange={(e) => setDescription(e.target.value)} />

        <label>Attach evidence (optional)</label>
        <input type="file" accept="image/jpeg,image/png,image/webp,application/pdf"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        {file && (
          <p className="muted" style={{ fontSize: 12, marginTop: 4 }}>
            Selected: <strong>{file.name}</strong> ({Math.round(file.size / 1024)} KB · {file.type || "unknown type"})
          </p>
        )}

        <div className="btn-row">
          <button className="primary" onClick={submit} disabled={busy || !policyId}>
            {busy ? "Submitting… (first request can take ~20s if the service is waking)" : "Submit claim"}
          </button>
        </div>
        <p className="muted" style={{ fontSize: 12, marginTop: 12 }}>
          Tip: amounts ≤ ₹1,00,000 with a clean fraud signal auto-approve and settle; larger or risky claims route to an adjuster.
        </p>
      </div>
    </Section>
  );
}
