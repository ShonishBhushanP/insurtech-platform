import { BrowserRouter, NavLink, Navigate, Route, Routes, useNavigate } from "react-router-dom";
import { AuthProvider, useAuth, type Role } from "./auth";
import Login from "./pages/Login";
import FileClaim from "./pages/FileClaim";
import RecentClaims from "./pages/RecentClaims";
import ClaimDetails from "./pages/ClaimDetails";
import FraudAlerts from "./pages/FraudAlerts";
import UnderwritingQueue from "./pages/UnderwritingQueue";
import MyPolicies from "./pages/MyPolicies";
import NewPolicy from "./pages/NewPolicy";
import PartnerDashboard from "./pages/PartnerDashboard";
import CompliancePortal from "./pages/CompliancePortal";

// Nav links per persona (the architecture's micro-frontends, role-switched in one shell).
const NAV: Record<Role, { to: string; label: string }[]> = {
  customer: [
    { to: "/file-claim", label: "File a Claim" },
    { to: "/claims", label: "My Claims" },
    { to: "/policies", label: "My Policies" },
  ],
  agent: [
    { to: "/policies", label: "Policies" },
    { to: "/policies/new", label: "Onboard Policy" },
    { to: "/file-claim", label: "File a Claim" },
    { to: "/claims", label: "Claims" },
  ],
  adjuster: [
    { to: "/fraud", label: "Fraud & Risk Alerts" },
    { to: "/underwriting", label: "Underwriting Queue" },
  ],
  partner: [
    { to: "/partner", label: "Cashless Authorization" },
  ],
  compliance: [
    { to: "/compliance", label: "Audit & Reports" },
  ],
};

const HOME: Record<Role, string> = {
  customer: "/claims", agent: "/policies", adjuster: "/fraud", partner: "/partner", compliance: "/compliance",
};

function Shell() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();
  if (!session) return <Login />;

  const signOut = () => { logout(); navigate("/", { replace: true }); };

  return (
    <>
      <header className="app-header">
        <div className="brand">🛡️ InsurTech <small>Digital Insurance Platform</small></div>
        <nav className="nav">
          {NAV[session.role].map((n) => (
            <NavLink key={n.to} to={n.to} className={({ isActive }) => (isActive ? "active" : "")}>{n.label}</NavLink>
          ))}
        </nav>
        <div className="role-switch" style={{ alignItems: "center", gap: 10 }}>
          <span style={{ fontSize: 13 }}>{session.name} · <strong style={{ textTransform: "capitalize" }}>{session.role}</strong></span>
          <button onClick={signOut}>Sign out</button>
        </div>
      </header>
      <main className="container">
        <Routes>
          <Route path="/" element={<Navigate to={HOME[session.role]} replace />} />
          <Route path="/file-claim" element={<FileClaim />} />
          <Route path="/claims" element={<RecentClaims />} />
          <Route path="/claims/:id" element={<ClaimDetails />} />
          <Route path="/policies" element={<MyPolicies />} />
          <Route path="/policies/new" element={<NewPolicy />} />
          <Route path="/fraud" element={<FraudAlerts />} />
          <Route path="/underwriting" element={<UnderwritingQueue />} />
          <Route path="/partner" element={<PartnerDashboard />} />
          <Route path="/compliance" element={<CompliancePortal />} />
          <Route path="*" element={<Navigate to={HOME[session.role]} replace />} />
        </Routes>
      </main>
    </>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Shell />
      </BrowserRouter>
    </AuthProvider>
  );
}
