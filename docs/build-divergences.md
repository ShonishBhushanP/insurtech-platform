# How the Actual Build Diverged from the Plan (demo retrospective)

A plan survives contact with a real (constrained) cloud only so far. This is what actually happened
between "here's the prompt" and "it works on Azure" — the surprises, the root causes, and how the
architecture made each one a small fix instead of a rebuild. **The recurring theme: because every
dependency sat behind a stable interface with a local default, every constraint became a config
switch or a one‑line fix — never a redesign.**

---

## 1. The sandbox had no RBAC — so half the diagram wasn't usable as drawn
- **Plan:** AKS, Managed Identity everywhere, APIM, Azure ML endpoint.
- **Reality:** the learner subscription is **Contributor without `roleAssignments/write`** — can't
  grant any RBAC. That rules out AKS integrations, Managed‑Identity access to SQL/Service Bus/Key
  Vault, and APIM‑Premium.
- **Fix:** **Azure Container Apps** instead of AKS (managed K8s, scale‑to‑zero, no cluster RBAC),
  **ACR admin creds** instead of AcrPull, **connection strings/SAS** instead of MI. Documented in
  **ADR‑002**. The app code never changed — only which adapter/credential the config selected.

## 2. Azure OpenAI was gated; Document Intelligence wasn't (but I assumed both were equal)
- **Plan:** Azure OpenAI for summarisation, Document Intelligence for OCR.
- **Reality:** **Azure OpenAI is access‑gated** — not grantable on a sandbox (a *hard* block). Document
  Intelligence turned out to be **available** (standard service, even a free F0 tier) — so that
  substitution was a *choice*, not a necessity.
- **Fix:** **Claude** behind `ILlmClient` (OpenAI substitute) and **Claude vision** for OCR; later
  wired **real Document Intelligence** too. ADR‑001 records the distinction honestly.

## 3. The corporate proxy blocked the build uploads
- **Surprise:** `az acr build` failed with a Zscaler block page — the proxy blocks Azure **data‑plane**
  uploads from my laptop.
- **Fix:** deploy from **Azure Cloud Shell** (runs inside Azure, bypasses the proxy and the ~15‑min
  token expiry). Made the build scripts **resumable** so a token timeout mid‑build just resumes.

## 4. The proxy ALSO blocked the browser → API (the big one)
- **Surprise:** the deployed UI loaded but every API call hung. It worked on a phone hotspot →
  **Zscaler blocks the browser's calls to `*.azurecontainerapps.io`** but allows `*.azurestaticapps.net`.
- **Fix:** make the API ride the **allowed** origin — wired the gateway as a **Static Web Apps linked
  backend** so the browser only ever calls `…azurestaticapps.net/api/*`, which the SWA proxies to the
  gateway. No CORS, nothing for the proxy to block.
- **Sub‑bug:** the gateway returned 404 for `/api/...` until I called **`UseRouting()` explicitly** —
  minimal hosting otherwise inserts routing *before* my `/api`‑strip middleware, so it matched the
  un‑stripped path. One‑line ordering fix.

## 5. Linking the backend silently turned on auth
- **Surprise:** after `az staticwebapp backends link`, every gateway call returned **401**.
- **Root cause:** linking **auto‑enables EasyAuth** on the backend Container App (it assumes you use
  SWA's built‑in auth; our app does its own).
- **Fix:** `az containerapp auth update --enabled false`.

## 6. PowerShell paste artifacts cost real time
- **Surprise:** backtick line‑continuations broke on paste → `Set-Location: cannot find parameter
  'ResourceGroup'`; and a mangled SQL connection string deployed with a **placeholder host**, so
  services crash‑looped with `SocketException: Name or service not known` (DNS couldn't resolve the
  SQL server).
- **Fix:** run commands as single lines; rewrite the `sql-cs` secret with the correct FQDN and roll a
  new revision. Lesson: **prefer dynamic discovery** (`az ... --query fqdn`) over pasted values.

## 7. Cognitive Services soft‑delete + the one‑free‑account rule
- **Surprise:** re‑creating `insurtech-docintel` failed — **soft‑deleted** (48h retention) and still
  **holding the single allowed F0 slot**, even though `list-deleted` showed nothing.
- **Fix:** create under a new name on **S0** (pay‑per‑page, pennies) — which also *guarantees* the
  `keyValuePairs` add‑on F0 may withhold. Sidestepped the whole soft‑delete tangle.

## 8. Durable Functions cold start beat the trigger timeout
- **Surprise:** switched adjudication to Durable Functions; the claim stuck at `Filed`.
- **Root cause:** the trigger's HTTP client has a **10s timeout**; a **cold Consumption Function**
  takes longer, the POST threw, and since the outbox row was already marked dispatched, **it never
  retried**. Consumption also can't stay warm (no "Always On").
- **Decision:** keep the **in‑process** saga for the live demo (instant, reliable); Durable Functions
  stays **deployed + documented** (ADR‑003) and shown warm. The `IAdjudicationTrigger` seam made this
  a pure config toggle.

## 9. deploy-functions.ps1 was Windows‑only
- **Surprise:** in Linux Cloud Shell it failed building `functions.zip`.
- **Root cause:** a hard‑coded `C:\Program Files\dotnet\dotnet.exe` and backslash paths.
- **Fix:** resolve `dotnet` from PATH; use forward‑slash paths. (Same lesson as the proxy CA bundle —
  scripts must be cross‑platform for Cloud Shell.)

## 10. Container Apps scale‑to‑zero cold starts
- **Surprise:** the first request after idle timed out the UI; CORS preflight needs a warm gateway.
- **Fix:** `min-replicas=1` on the gateway + core services for the demo (`scale-apps.sh`), scale back
  to 0 afterwards for cost.

## 11. I deleted all the resources by accident
- **Reality:** a cleanup wiped the whole resource group's contents the day before the demo.
- **Fix:** because everything is **IaC + scripted**, a single ordered runbook (`RECREATE-ALL.md`)
  rebuilt SQL → SWA → backend → linked backend → AI/messaging from scratch. The only manual values
  were a password and a key. (New environment domain on rebuild didn't matter — the UI is same‑origin
  and scripts fetch FQDNs dynamically.)

---

## The takeaway (say this in the demo)
None of these were redesigns. Every constraint hit a **seam I'd already built** — a config‑switchable
adapter, a graceful‑degrade fallback, a fail‑open path, or an IaC script — so the response was a
**toggle, a substitute, or a one‑line fix**. That's the real argument for the architecture:
> *Designed Azure‑native; deployed constraint‑appropriate; reversible by configuration.*

The substitutions (Claude/ML.NET, Container Apps, in‑process saga, SAS Service Bus) aren't compromises
that hide the design — they **prove** it, because flipping back to the diagram's target is a config
change, not a code change. See `docs/adr-001/002/003`.
