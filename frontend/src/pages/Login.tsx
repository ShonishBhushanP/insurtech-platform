import { useAuth, type Session } from "../auth";

// Mock persona sign-in (stand-in for Microsoft Entra ID / Entra External ID OIDC).
// Each persona maps to one of the architecture's micro-frontends.
const personas: { title: string; desc: string; icon: string; session: Session }[] = [
  {
    title: "Customer Portal", icon: "👤",
    desc: "Buy & manage policies, file and track claims.",
    session: { role: "customer", userId: "usr_8b2", name: "R. Sharma", channel: "WebMFE" },
  },
  {
    title: "Agent / Broker Portal", icon: "🧑‍💼",
    desc: "Onboard policies and file claims on behalf of customers.",
    session: { role: "agent", userId: "usr_8b2", name: "A. Broker", channel: "AgentBFF" },
  },
  {
    title: "Claims Adjuster Workbench", icon: "🕵️",
    desc: "Review fraud & risk alerts and the underwriting queue.",
    session: { role: "adjuster", userId: "", name: "K. Adjuster", channel: "Staff" },
  },
];

export default function Login() {
  const { login } = useAuth();
  return (
    <div style={{ maxWidth: 920, margin: "60px auto", padding: "0 24px" }}>
      <div style={{ textAlign: "center", marginBottom: 28 }}>
        <h1 style={{ fontSize: 30, margin: 0 }}>🛡️ InsurTech</h1>
        <p className="muted">Digital Insurance Claims &amp; Policy Management Platform — choose how to sign in</p>
      </div>
      <div className="grid cols-3">
        {personas.map((p) => (
          <div key={p.title} className="card" style={{ cursor: "pointer", textAlign: "center" }} onClick={() => login(p.session)}>
            <div style={{ fontSize: 40 }}>{p.icon}</div>
            <h3 style={{ margin: "8px 0 4px" }}>{p.title}</h3>
            <p className="muted" style={{ fontSize: 13, minHeight: 48 }}>{p.desc}</p>
            <button className="primary" style={{ width: "100%" }}>Sign in</button>
          </div>
        ))}
      </div>
      <p className="muted" style={{ textAlign: "center", fontSize: 12, marginTop: 24 }}>
        Demo sign-in. In production this is OpenID Connect via <strong>Microsoft Entra ID</strong> (staff/agents)
        and <strong>Entra External ID / B2C</strong> (customers); the token's roles/scopes drive the persona.
      </p>
    </div>
  );
}
