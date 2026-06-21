import { StrictMode, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import { MsalProvider } from "@azure/msal-react";
import App from "./App";
import { entraEnabled, msalInstance } from "./msal";
import "./styles.css";

async function bootstrap() {
  let tree: ReactNode = <App />;

  // Wrap in the MSAL provider only when Entra is configured (otherwise the demo login is used).
  if (entraEnabled && msalInstance) {
    await msalInstance.initialize();
    await msalInstance.handleRedirectPromise();
    tree = <MsalProvider instance={msalInstance}>{tree}</MsalProvider>;
  }

  createRoot(document.getElementById("root")!).render(<StrictMode>{tree}</StrictMode>);
}

void bootstrap();
