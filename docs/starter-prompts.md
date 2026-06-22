# Starter Prompts — kick off the InsurTech build (local‑first, minimal substitution)

Short prompts I send **one at a time** to start the build. The philosophy: **build it working and
local first**, with the *smallest* substitution footprint, and only add Azure later. Each prompt
assumes the agent can read my `Final Docs/` (HLD, LLD, API spec, Azure deployment diagram).

> Minimal substitution rule for the start: **one interface per external dependency, but implement
> only the local/in‑memory side now.** No Azure, no keys, no cloud. We layer Azure adapters in a
> later phase, behind the same interfaces, without touching call sites.

---

### Prompt 0 — context
> Read everything in `Final Docs/` (HLD, LLD, API spec, the Azure deployment diagram). Summarise the
> system, the services, and the key flows back to me in one page, and list what you'll build first.
> Don't write code yet.

### Prompt 1 — scaffold
> Create a new folder `insurtech-platform/`. Scaffold a .NET 8 solution: a `BuildingBlocks` library
> (host defaults, RFC‑7807 problem details, an in‑memory event bus behind an `IEventBus` interface),
> a YARP **API gateway** (single local origin, reverse‑proxy), and one minimal **Policy** service
> with a health endpoint. Target `net8.0`; don't install the SDK — I have it. Make it run locally.

### Prompt 2 — the showcase service (Claims)
> Build the **Claims** service in Clean Architecture (Domain / Application / Infrastructure / Api).
> Implement the claim aggregate and the adjudication saga `Filed → Triaged → Approved/Referred → Paid`
> from the LLD. Use a **transactional outbox** with a background dispatcher, **idempotency keys** on
> POSTs, and run the saga **in‑process** for now. Persist with **EF InMemory**. Add xUnit tests for
> the aggregate. Trace code comments to LLD/FR ids.

### Prompt 3 — the rest of the services
> Add the remaining services as minimal ASP.NET hosts behind the gateway: **Fraud, Documents,
> Payments, Partner, Underwriting, Notification, Audit**. Keep them in‑memory/stub. For Fraud, give me
> a simple heuristic score 0–1 + a block/refer/allow decision. For Documents, a stub OCR that returns
> plausible fields. Wire Claims → Fraud/Payments through the gateway.

### Prompt 4 — frontend
> Build a React 18 + Vite + TypeScript app: one shell with **persona‑based navigation** (Customer,
> Agent, Adjuster, Partner, Compliance) and a simple login that picks a persona. Pages: My Policies,
> New Policy, File a Claim (with a real file‑upload control), Recent Claims, Claim Details, Fraud
> Alerts. A typed API client against `VITE_API_BASE` (default `http://localhost:8080`).

### Prompt 5 — run it
> Give me one command per side to run the whole thing locally (backend + frontend), and walk me
> through filing a claim end‑to‑end. Fix anything that doesn't work until a claim adjudicates.

---

## Later (only when I ask) — layer Azure behind the same interfaces

Keep these as **separate, additive** prompts so the local build never breaks:

### Prompt A1 — adapter seams
> For each external dependency add an Azure adapter **behind the existing interface**, selected by
> config, with the local impl as the default when config is absent: EF InMemory ↔ **Azure SQL**,
> in‑process bus ↔ **Service Bus**, stub OCR ↔ **Document Intelligence / Claude vision**, heuristic
> ↔ **ML.NET / Azure ML**, console ↔ **App Insights**. Don't change any call sites.

### Prompt A2 — IaC + deploy
> Add **Bicep** (a production path with Managed Identity/RBAC, and a **sandbox‑safe** no‑RBAC variant)
> and **Cloud‑Shell** deploy scripts: Azure SQL, backend → **Container Apps** (+ ACR build), frontend
> → **Static Web Apps**. Make the scripts resumable and Linux‑Cloud‑Shell‑safe.

### Prompt A3 — the AI features for real
> Wire **Claude** for OCR (vision) and fraud summarisation behind the existing interfaces; keys from
> config/secrets, never hard‑coded; everything degrades gracefully to the local stub.

### Prompt A4 — workflow + observability (optional)
> Add the **Durable Functions** adjudication orchestrator as an alternative to the in‑process saga
> (config‑switched), and wire App Insights. Document both.

---

**Why local‑first / minimal substitution:** I get a working, demoable system fast with nothing to
provision, then I add Azure piece‑by‑piece behind stable seams — so a missing key, a blocked proxy,
or a budget limit never stops the app from running. The full requirements live in `build-prompt.md`.
