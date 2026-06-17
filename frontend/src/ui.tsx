import type { ReactNode } from "react";

export const DEMO_USER = "usr_8b2"; // seeded policyholder (Policy service)

export function money(amount: number | null | undefined, currency = "INR"): string {
  if (amount == null) return "—";
  return new Intl.NumberFormat("en-IN", { style: "currency", currency, maximumFractionDigits: 0 }).format(amount);
}

export function when(iso: string): string {
  return new Date(iso).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" });
}

const STATUS_TONE: Record<string, string> = {
  Filed: "blue", Triaged: "blue", UnderInvestigation: "amber",
  ReferredForUnderwriting: "amber", Approved: "green", Paid: "green",
  Rejected: "red", Cancelled: "gray", Closed: "gray",
  Open: "amber", UnderReview: "amber", ConfirmedFraud: "red", ConfirmedLegit: "green",
  High: "red", Medium: "amber", Low: "green",
};

export function Badge({ value }: { value: string }) {
  const tone = STATUS_TONE[value] ?? "gray";
  return <span className={`badge ${tone}`}>{value}</span>;
}

// Visual claim state progression (UI brief screen #2).
const LIFECYCLE = ["Filed", "Triaged", "Approved", "Paid"];
export function LifecycleStepper({ status }: { status: string }) {
  const terminalReject = status === "Rejected" || status === "Cancelled";
  const investigation = status === "UnderInvestigation" || status === "ReferredForUnderwriting";
  const stages = investigation ? ["Filed", "Triaged", "In Review", "Decision"] : LIFECYCLE;
  const currentIdx = (() => {
    if (investigation) return 2;
    const i = LIFECYCLE.indexOf(status);
    return i >= 0 ? i : 0;
  })();

  return (
    <div className="stepper">
      {stages.map((s, i) => {
        const cls = i < currentIdx ? "done" : i === currentIdx ? "current" : "";
        return (
          <span key={s} style={{ display: "flex", alignItems: "center" }}>
            <span className={`step ${cls}`}>
              <span className="dot" style={terminalReject && i >= currentIdx ? { background: "#dc2626" } : undefined}>
                {i < currentIdx ? "✓" : i + 1}
              </span>
              {s}
            </span>
            {i < stages.length - 1 && <span className="arrow">→</span>}
          </span>
        );
      })}
    </div>
  );
}

export function Section({ title, sub, children }: { title: string; sub?: string; children: ReactNode }) {
  return (
    <>
      <h1 className="page-title">{title}</h1>
      {sub && <p className="page-sub">{sub}</p>}
      {children}
    </>
  );
}
