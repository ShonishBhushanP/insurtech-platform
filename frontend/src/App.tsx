import { useState } from "react";
import { BrowserRouter, NavLink, Navigate, Route, Routes, useNavigate } from "react-router-dom";
import FileClaim from "./pages/FileClaim";
import RecentClaims from "./pages/RecentClaims";
import ClaimDetails from "./pages/ClaimDetails";
import FraudAlerts from "./pages/FraudAlerts";
import UnderwritingQueue from "./pages/UnderwritingQueue";

type Role = "customer" | "adjuster";

function Header({ role, setRole }: { role: Role; setRole: (r: Role) => void }) {
  const nav = useNavigate();
  function switchRole(r: Role) {
    setRole(r);
    nav(r === "customer" ? "/claims" : "/fraud");
  }
  return (
    <header className="app-header">
      <div className="brand">
        🛡️ InsurTech <small>Digital Insurance Claims &amp; Policy Platform</small>
      </div>
      <nav className="nav">
        {role === "customer" ? (
          <>
            <NavLink to="/file-claim" className={({ isActive }) => (isActive ? "active" : "")}>File a Claim</NavLink>
            <NavLink to="/claims" className={({ isActive }) => (isActive ? "active" : "")}>My Claims</NavLink>
          </>
        ) : (
          <>
            <NavLink to="/fraud" className={({ isActive }) => (isActive ? "active" : "")}>Fraud &amp; Risk Alerts</NavLink>
            <NavLink to="/underwriting" className={({ isActive }) => (isActive ? "active" : "")}>Underwriting Queue</NavLink>
          </>
        )}
      </nav>
      <div className="role-switch">
        <button className={role === "customer" ? "active" : ""} onClick={() => switchRole("customer")}>Customer Portal</button>
        <button className={role === "adjuster" ? "active" : ""} onClick={() => switchRole("adjuster")}>Adjuster Workbench</button>
      </div>
    </header>
  );
}

export default function App() {
  const [role, setRole] = useState<Role>(() => (localStorage.getItem("role") as Role) || "customer");
  function update(r: Role) { setRole(r); localStorage.setItem("role", r); }

  return (
    <BrowserRouter>
      <Header role={role} setRole={update} />
      <main className="container">
        <Routes>
          <Route path="/" element={<Navigate to={role === "customer" ? "/claims" : "/fraud"} replace />} />
          <Route path="/file-claim" element={<FileClaim />} />
          <Route path="/claims" element={<RecentClaims />} />
          <Route path="/claims/:id" element={<ClaimDetails />} />
          <Route path="/fraud" element={<FraudAlerts />} />
          <Route path="/underwriting" element={<UnderwritingQueue />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </BrowserRouter>
  );
}
