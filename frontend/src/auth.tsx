import { createContext, useContext, useState, type ReactNode } from "react";

// Persona model. In production these come from a Microsoft Entra ID / Entra External ID (B2C)
// OIDC token (roles/scopes claims); here a mock login sets the session so the persona UIs work.
export type Role = "customer" | "agent" | "adjuster";

export interface Session {
  role: Role;
  userId: string;   // the customer the session acts as (empty for staff)
  name: string;
  channel: string;  // FNOL channel sent to the API (WebMFE / AgentBFF / …)
}

const KEY = "insurtech.session";

interface AuthCtx {
  session: Session | null;
  login: (s: Session) => void;
  logout: () => void;
}

const Ctx = createContext<AuthCtx>(null!);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(() => {
    const raw = localStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as Session) : null;
  });
  const login = (s: Session) => { localStorage.setItem(KEY, JSON.stringify(s)); setSession(s); };
  const logout = () => { localStorage.removeItem(KEY); setSession(null); };
  return <Ctx.Provider value={{ session, login, logout }}>{children}</Ctx.Provider>;
}

export const useAuth = () => useContext(Ctx);
