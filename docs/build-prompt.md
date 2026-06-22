# Build Prompt — InsurTech Digital Insurance Platform (Phase 3 Execution)

> This is the instruction set I would give an AI engineering agent to design and build
> this platform from scratch. 

---

## 1. Role and goal

You are my senior engineering partner. Build me a **runnable Digital Insurance Claims & Policy
Management Platform** — codename **"InsurTech"** — as the **Phase 3 (Execution)** deliverable of an
"Upgrade to Architect" program. Phase 1 produced the HLD and Phase 3 produced the LLD; your job is to
turn those designs into working, deployable code that demonstrates the architecture end‑to‑end.

Optimise for: faithful realisation of the design, a system that **runs locally with zero external
dependencies**, and a **documented, low‑risk path to the Azure‑native target**. Favour clear,
idiomatic code and strong architectural seams over cleverness.

## 2. Source of truth — read these first

Everything you build must trace back to my design artifacts in **`Final Docs/`**:
- **HLD** (Phase 1) — context, container, and high‑level component views; quality attributes.
- **LLD** (Phase 3) — component/sequence designs, the appendix service specs (A.1–A.4), TR/FR/IR
  requirement IDs. Cite these IDs in code comments (e.g. "LLD A.1.8", "FR‑050").
- **API specification** — REST contracts, status codes, idempotency, RFC‑7807 problem details.
- **Azure deployment diagram** (`*Azure_Deployment.png`) — the target Azure topology. Treat each box
  (edge, compute, data, AI/ML, shared platform) as a tier you must represent.

If the docs and a sensible default conflict, follow the docs and note the deviation.

## 3. Tech stack (fixed)

- **Backend:** .NET 8, C#, ASP.NET Core Minimal APIs. (Build with whatever SDK is installed — target
  `net8.0`; if only a newer SDK is present, use `<RollForward>Major</RollForward>`. **Do not install
  the SDK yourself** — I install it manually.)
- **Frontend:** React 18 + Vite + TypeScript, React Router.
- **Cloud:** Azure. **Both** an in‑app config‑switchable Azure adapter layer **and** Bicep IaC.
- **Tests:** xUnit for the domain/aggregate logic.

## 4. Architecture and solution layout

One backend solution with:
- **BuildingBlocks** — shared cross‑cutting: service defaults/host wiring, idempotency, RFC‑7807
  problem details, messaging abstraction + outbox, persistence store‑selector, AI (`ILlmClient`),
  telemetry, Key Vault config, `DefaultAzureCredential` helper.
- **API Gateway** — a single public origin that reverse‑proxies to the services (YARP). This is the
  local stand‑in for Azure API Management.
- **9 microservices**, each its own ASP.NET host: **Policy, Claims, Fraud, Documents (DocMgmt),
  Payments, Partner, Underwriting, Notification, Audit**. (The diagram named eight; implement the
  missing **Underwriting, Notification, Audit** too.)
- **Claims** is the showcase service — build it in **Clean Architecture** layers
  (Domain / Application / Infrastructure / Api) plus a **Durable Functions** project.
- A **unit test** project for the Claims aggregate/saga.

## 5. Cross‑cutting patterns (apply throughout)

- **Transactional outbox + background dispatcher** — domain events persisted in the same DB
  transaction as the aggregate; a dispatcher tails undispatched rows and publishes them, then stamps
  dispatched.
- **Idempotency keys** on all unsafe POSTs.
- **RFC‑7807 problem details** for all errors, with stable error codes (e.g. `FR‑001`).
- **Domain events** and a CQRS‑ish handler style in Claims.
- **Fail‑open** on the fraud hot path (FR‑050): if scoring is slow/unavailable, never block the claim.
- **Idempotent, replayable** claim adjudication saga: `Filed → Triaged → (Fraud) → Approved/Referred/
  Investigated → Paid`.

## 6. The core principle — config‑switchable adapters with a local default

**This is the most important design rule.** Every external dependency must have **two
implementations behind one interface**, selected by configuration at startup:
- a **local/in‑process default** (so the whole platform runs offline with no Azure, no keys), and
- an **Azure‑native adapter** activated only when its config is present.

Implement these switchable seams (local ↔ Azure):
- Persistence: **EF InMemory ↔ Azure SQL** (`ConnectionStrings__*`).
- Messaging: **in‑process bus ↔ Azure Service Bus** (`Azure:ServiceBus:*`).
- Idempotency store: **in‑memory ↔ Redis**.
- Document storage: **loopback stub ↔ Blob (user‑delegation SAS)**.
- Doc metadata: **in‑memory ↔ Cosmos**.
- Telemetry: **console ↔ Application Insights / Azure Monitor** (connection‑string based).
- Secrets/config: local config ↔ **Key Vault** via Managed Identity.
- Auth: app‑level + optional **Entra/MSAL** (config‑gated on the frontend).

All Azure data‑plane access should prefer **Managed Identity** (`DefaultAzureCredential`) in the
production path, with a connection‑string/key fallback for constrained environments.

## 7. AI/ML tier (deployment diagram: AI/ML)

Implement each capability behind an interface with a priority‑ordered engine selection:
- **OCR / form recognition** (`IDocumentExtraction`): **Azure Document Intelligence** (REST,
  prebuilt‑layout + keyValuePairs) → **Claude vision** (multimodal Messages API, reads the real
  bytes) → **local stub** (derives byte attributes + type‑appropriate canned values; classifies the
  document from filename/sensitivity so it never emits, e.g., vehicle‑damage fields for a death
  certificate). Every engine must **degrade gracefully** — never fail the upload.
- **Fraud scoring** (`IFraudScoringEngine`): **Azure ML endpoint** → **custom ML.NET model**
  (FastTree, trained in‑process on synthetic data, with SHAP‑style feature contributions) →
  **heuristic** fallback. Emit a score 0–1 + a decision band (block/refer/allow).
- **Fraud analysis / summarization** (`ILlmClient`): **Claude** (substitutes for Azure OpenAI) →
  deterministic **rule‑based** brief. Always report `generatedBy` provenance.

LLM/OCR keys come from config/Key Vault — **never hard‑coded**.

## 8. Claims workflow — Durable Functions with an in‑process default

Model adjudication as an **Azure Durable Functions** orchestration (LLD A.1.3.2): an HTTP starter
schedules an orchestrator whose activities call Fraud → Triage → Approve/Refer → Pay. Also implement
an **in‑process** trigger that runs the identical saga inside the Claims service. Select via
`Claims:Adjudication:Mode` (`InProcess` default, `DurableFunctions` when a Functions base URL is set).
The outbox dispatcher fires the chosen trigger on `ClaimFiled`.

## 9. Frontend — one shell, role‑switched micro‑frontends

A single React app that presents **persona‑specific navigation and pages** (the architecture's
micro‑frontends, role‑switched in one shell): **Customer, Agent, Adjuster, Partner, Compliance**.
- **Login** with selectable personas; wire **real Entra/MSAL** sign‑in, **config‑gated** (falls back
  to the persona picker when Entra env vars are absent).
- Pages: My Policies, New Policy (premium calc → `POST /v1/policies`), File a Claim (with a **real
  file upload** control → upload‑url → bytes → OCR pipeline), Recent Claims, Claim Details (status
  stepper, fraud/risk, **document preview + extracted OCR fields**, AI analysis), Fraud Alerts,
  Underwriting Queue, Partner cashless authorization, Compliance audit/verify/report.
- A typed API client with timeouts and clear error surfacing. `VITE_API_BASE` configurable.

## 10. Infrastructure & deployment

- **Bicep**: a full production path (platform + apps + edge, with Managed Identity/RBAC, APIM/AKS
  intent) **and** a **sandbox‑safe variant** (no RBAC) for a constrained subscription.
- **Compute:** deploy services to **Azure Container Apps** (the AKS stand‑in: managed K8s + KEDA +
  Envoy ingress, scale‑to‑zero). Gateway has external ingress; services internal.
- **Frontend:** **Azure Static Web Apps** (+ a GitHub Action), with SPA `navigationFallback`.
- **Registry:** ACR (admin credentials in the sandbox; RBAC AcrPull in production).
- **Deploy scripts** (PowerShell, runnable from **Azure Cloud Shell**): SQL bicep, backend build
  (`az acr build` per image → deploy), Static Web App, Durable Functions zip‑deploy. Scripts must be
  **resumable** and work on Linux Cloud Shell (resolve `az`/`dotnet` from PATH, forward‑slash paths).

## 11. Hard constraints I'm operating under (design for these)

I'm deploying on an Azure **learner sandbox**. Build so these never block a working demo:
- **Contributor only — no `Microsoft.Authorization/roleAssignments/write`.** So: no Managed‑Identity
  RBAC, no AKS, no APIM‑Premium. Use admin creds / connection strings / SAS instead, and keep the
  RBAC path as the documented production target.
- **Azure OpenAI is access‑gated** (unavailable) → use Claude behind `ILlmClient`.
- **Budget‑sensitive** → prefer scale‑to‑zero and per‑transaction services; avoid always‑on compute
  (e.g. Azure ML endpoints); make it easy to delete/scale‑down after a demo.
- **~15‑min sandbox token expiry + corporate proxy (Zscaler)** blocks data‑plane uploads from my
  laptop → deploy from **Cloud Shell**; make builds resumable.
- **The corporate proxy blocks the browser's calls to `*.azurecontainerapps.io`** but allows
  `*.azurestaticapps.net`. So the deployed UI must reach the API **same‑origin**: wire the gateway as
  a **Static Web Apps linked backend** and have the gateway strip the `/api` prefix (call
  `UseRouting()` explicitly so routing sees the stripped path). The frontend calls `/api/*`.

## 12. Decisions I want recorded (ADRs)

Where you substitute the Azure‑native target for a constraint‑appropriate alternative, write a short
ADR (context / decision / why / trade‑offs / how to switch back). At minimum:
- **AI/ML substitutions** (Claude + ML.NET vs Azure OpenAI / Document Intelligence / Azure ML) —
  distinguish *hard blocks* (Azure OpenAI) from *cost/operational choices* (DI is available on F0).
- **Compute & messaging** (Container Apps vs AKS; Service Bus via SAS connection string, **not Dapr**).
- **Claims workflow** (in‑process vs Durable Functions).
Every ADR's theme: a **stable interface, a constraint‑appropriate default, and a config‑only path to
the architecture's target**.

## 13. Deliverables

1. The full solution (backend services + gateway + Functions + frontend) that **runs locally with one
   command per side**, no Azure required.
2. Bicep + Cloud‑Shell deploy/runbook scripts, including a **recreate‑from‑scratch** runbook and a
   **scale (always‑on / scale‑to‑zero)** helper.
3. Docs: the ADRs above, an **AI pipelines** explainer, a **fraud model card** + training explainer,
   an **Entra login** guide, and a same‑origin‑proxy runbook.
4. Sample documents (death certificate, medical bill, repair quote, etc.) that the OCR can read.
5. Unit tests for the Claims aggregate/saga (all green).
6. A GitHub repo with clean, descriptive commit history.

## 14. How I want you to work

- **Don't install the .NET SDK** — I do that. **Don't paste secrets in chat**; put keys in config/
  secrets/Key Vault. Treat sandbox SQL passwords as throwaways and remind me to rotate shared creds.
- Commit and push only when I ask or when a unit of work is complete; write clear messages.
- When something is hard‑to‑reverse or outward‑facing (deploys, deletes), confirm first.
- Be honest in status reports — if a step is a substitute or a known limitation, say so plainly and
  give me the production‑faithful alternative.
- Prefer dynamic discovery in scripts (fetch FQDNs/ids at runtime) over hard‑coded values.

## 15. Acceptance criteria

- Local: backend up, frontend up, a claim can be filed and adjudicated end‑to‑end with the in‑process
  saga, fraud scoring + OCR + audit chain all working on stubs/in‑memory.
- Azure: services on Container Apps reachable through the gateway; UI on Static Web Apps reaching the
  API **same‑origin**; Azure SQL persistence verified; at least one AI capability demonstrable live
  (Claude OCR/fraud) with the Azure‑native engines one config switch away.
- Every requirement traces to an LLD/FR/TR id in comments; every Azure substitution has an ADR.

Build it incrementally, keep it runnable at every step, and tell me what you'd do differently if I
had an unconstrained (Owner‑level, funded) subscription.
