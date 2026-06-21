# Real Sign-In with Microsoft Entra ID (MSAL)

The frontend supports **two login modes**, chosen automatically by configuration:

- **Demo persona login** (default) — pick Customer / Agent / Adjuster / Partner / Compliance. No setup.
- **Microsoft Entra ID** — when an app registration is configured, the login screen shows
  **"Sign in with Microsoft Entra ID"** (MSAL popup); the persona is derived from the token's
  app‑role claims.

Switching is just config — no code change (`src/msal.ts` checks the env vars).

## Enable Entra login

### 1. Register the SPA in Entra ID
Portal → **Microsoft Entra ID → App registrations → New registration**:
- Name: `InsurTech Web`
- Supported account types: as appropriate (single tenant for staff; *External ID/B2C* for customers)
- **Redirect URI:** *Single-page application (SPA)* → your site origin, e.g.
  `https://red-water-0a9d64f00.7.azurestaticapps.net` (and `http://localhost:5173` for dev)

Copy the **Application (client) ID** and **Directory (tenant) ID**.

### 2. (Optional) Define app roles for personas
App registration → **App roles** → create roles whose *value* is one of:
`Agent`, `Adjuster`, `Partner`, `Compliance` (anything else ⇒ Customer). Assign users to roles in
**Enterprise applications → Users and groups**. The frontend maps the token's `roles` claim to the
persona (`roleFromClaims` in `Login.tsx`); with no role assigned a user defaults to **Customer**.

### 3. Configure the frontend
Set these (in `frontend/.env` for local, or as build env in the GitHub Actions workflow):
```
VITE_ENTRA_CLIENT_ID=<application-client-id>
VITE_ENTRA_TENANT_ID=<directory-tenant-id>
VITE_ENTRA_SCOPES=User.Read           # optional, comma-separated
```
Rebuild (`npm run build`) / push. The login screen now shows the **Entra sign-in** button above the
demo personas.

## Scope: what this does / doesn't do
- **Does:** authenticate the user against Entra (real OIDC popup), read their identity + app roles,
  and drive the persona UI.
- **Doesn't (yet):** attach the access token to API calls + validate it at the backend. In the target
  architecture that's enforced at **APIM** (JWT validation) with per‑service scope/owner checks. On
  the learner sandbox APIM isn't available, so the deployed APIs remain open; the production path is to
  front the gateway with APIM and turn on token validation. To send the token from the SPA, acquire it
  with `instance.acquireTokenSilent({scopes})` and add `Authorization: Bearer <token>` in `api.ts`.

## Sandbox note
Learner/sandbox tenants often **block app registrations**. If you can't register an app there, use a
**personal Microsoft Entra tenant** (free) for the registration — the SPA can still point at the same
deployed backend. Until then, the **demo persona login** works with zero setup.
