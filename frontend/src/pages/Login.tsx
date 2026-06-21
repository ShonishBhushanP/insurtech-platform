import { useAuth, type Role, type Session } from "../auth";
import { useMsal } from "@azure/msal-react";
import { entraEnabled, loginScopes } from "../msal";

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
  {
    title: "Partner Dashboard", icon: "🏥",
    desc: "Hospital/garage cashless pre-authorization (B2B).",
    session: { role: "partner", userId: "", name: "Apollo Hospital", channel: "PartnerB2B" },
  },
  {
    title: "Admin / Compliance Portal", icon: "📋",
    desc: "Immutable audit trail and regulatory reports.",
    session: { role: "compliance", userId: "", name: "C. Officer", channel: "Staff" },
  },
];

// Maps Entra app-role claims to a persona (configure these app roles in the registration).
function roleFromClaims(roles: string[]): Role {
  const r = roles.map((x) => x.toLowerCase());
  if (r.includes("adjuster")) return "adjuster";
  if (r.includes("agent")) return "agent";
  if (r.includes("partner")) return "partner";
  if (r.includes("compliance")) return "compliance";
  return "customer";
}

function EntraSignIn() {
  const { instance } = useMsal();
  const { login } = useAuth();
  async function signIn() {
    const res = await instance.loginPopup({ scopes: loginScopes });
    const claims = (res.account?.idTokenClaims ?? {}) as { roles?: string[]; name?: string };
    login({
      role: roleFromClaims(claims.roles ?? []),
      userId: res.account?.localAccountId ?? "usr_8b2",
      name: res.account?.name ?? "Entra User",
      channel: "Entra",
    });
  }
  return (
    <div style={{ textAlign: "center", marginBottom: 22 }}>
      <button className="primary" style={{ padding: "12px 22px" }} onClick={signIn}>Sign in with Microsoft Entra ID</button>
      <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>or use a demo persona below</p>
    </div>
  );
}

export default function Login() {
  const { login } = useAuth();
  return (
    <div style={{ maxWidth: 920, margin: "60px auto", padding: "0 24px" }}>
      <div style={{ textAlign: "center", marginBottom: 28 }}>
        <h1 style={{ fontSize: 30, margin: 0 }}>🛡️ InsurTech</h1>
        <p className="muted">Digital Insurance Claims &amp; Policy Management Platform — choose how to sign in</p>
      </div>
      {entraEnabled && <EntraSignIn />}
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
