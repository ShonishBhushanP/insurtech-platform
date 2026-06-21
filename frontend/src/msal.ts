import { PublicClientApplication } from "@azure/msal-browser";

// Microsoft Entra ID sign-in, enabled only when an app registration is configured via env:
//   VITE_ENTRA_CLIENT_ID, VITE_ENTRA_TENANT_ID  (see docs/ENTRA-LOGIN.md)
// When unset, the app falls back to the demo persona login.
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID as string | undefined;
const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID as string | undefined;

export const entraEnabled = Boolean(clientId && tenantId);

export const loginScopes = (import.meta.env.VITE_ENTRA_SCOPES as string | undefined)?.split(",").map((s) => s.trim())
  ?? ["User.Read"];

export const msalInstance = entraEnabled
  ? new PublicClientApplication({
      auth: {
        clientId: clientId!,
        authority: `https://login.microsoftonline.com/${tenantId}`,
        redirectUri: window.location.origin,
      },
      cache: { cacheLocation: "localStorage" },
    })
  : null;
