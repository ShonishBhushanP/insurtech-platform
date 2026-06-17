# InsurTech — Digital Insurance Claims & Policy Management Platform

**Phase 3 (Execution) — working code companion to the Phase 1 HLD and Phase 3 LLD.**
Author: 615416 — Shonish Bhushan Palanisamy · Owner: InsurTech Architecture Office

This repository is a runnable, vertical-slice implementation of the Option‑C architecture
described in the design documents (`Final Docs/`). It demonstrates the architecturally
significant flows — FNOL claim filing, the adjudication saga, AI fraud scoring with
explainability, document intake, and settlement — across a .NET 8 microservices backend and a
React micro‑frontend shell.

> **Design fidelity vs. local runnability.** The code maps 1:1 to the LLD’s service boundaries,
> patterns (transactional outbox, idempotency, RFC‑7807 problem details, fail‑open fraud, domain
> events, CQRS‑style use cases), and API contracts. To run on a laptop with **zero Azure
> dependencies**, the Azure PaaS resources are substituted with local equivalents (see
> [What’s real vs. stubbed](#whats-real-vs-stubbed)). Each substitution is behind an interface so
> the production adapter drops in unchanged.

---

## Architecture at a glance

```
                React MFE shell (Vite)  ── Customer Portal · Adjuster Workbench
                          │  http
                          ▼
                 API Gateway (YARP)  ← local stand-in for Azure API Management Premium
                          │
   ┌──────────┬───────────┼───────────┬───────────┬───────────┐
   ▼          ▼           ▼           ▼           ▼           ▼
 Policy     Claims      Fraud      Documents   Payments    Partner
 :5101      :5102       :5103      :5104       :5105       :5106
            │  outbox → dispatcher → adjudication saga → Fraud + Payments
            └─ Clean Architecture: Domain · Application · Infrastructure · Api
```

| Service | Port | LLD ref | Key endpoints |
|---|---|---|---|
| **Policy** | 5101 | A.4 | `POST /v1/policies`, `GET /v1/policies` |
| **Claims Intake & Adjudication** | 5102 | A.1 *(deep)* | `POST /v1/claims`, `GET /v1/claims/{id}/status`, `POST /v1/claims/{id}/decision` |
| **Fraud Detection** | 5103 | A.2 *(deep)* | `POST /v1/fraud/score`, `GET /v1/fraud/cases`, `POST /v1/fraud/cases/{id}/decision` |
| **Document Management** | 5104 | A.3 *(deep)* | `POST /v1/documents/upload-url`, `GET /v1/documents/{id}` |
| **Payments & Settlement** | 5105 | A.6 | `POST /v1/payments` |
| **Partner Integration** | 5106 | A.7 | `POST /v1/partner/cashless/authorize` |
| **API Gateway** | 8080 | §4.1 (APIM) | reverse-proxies all of the above |

The seven endpoints match the **API Specification** (`Final Docs/…_API-Specification.docx`) 1:1.

---

## Tech stack

- **Backend:** .NET 8, ASP.NET Core Minimal APIs, EF Core (InMemory provider locally), YARP.
- **Frontend:** React 18 + TypeScript + Vite.
- **Patterns:** Clean Architecture (Claims), transactional outbox + dispatcher, idempotency keys,
  domain events, RFC‑7807 problem details, typed resilient HTTP clients, fail‑open fraud scoring.
- **Tests:** xUnit + FluentAssertions with Coverlet coverage.

---

## Prerequisites

- **.NET 8 SDK** — install from <https://dotnet.microsoft.com/download/dotnet/8.0> (only the
  .NET 8 *runtime* is required at minimum, but the **SDK** is needed to build/run). After
  installing, open a new terminal so `dotnet --version` reports `8.x`.
- **Node.js 18+** and npm (verified with Node 22).

---

## Running it

### 1. Backend (7 windows: 6 services + gateway)

```powershell
# from the repo root
./scripts/run-backend.ps1
```

Or build/test the whole solution first:

```powershell
./scripts/build-and-test.ps1          # restore + build + test + coverage
dotnet run --project backend/src/Services/Claims/Claims.Api   # run a single service
```

Each service exposes Swagger UI at `http://localhost:<port>/swagger` and a `GET /health`.

### 2. Frontend

```powershell
cd frontend
npm install
npm run dev          # http://localhost:5173
```

The frontend talks to the gateway at `http://localhost:8080` (configurable via `frontend/.env`).

---

## Demo walk-through

1. **Customer Portal → File a Claim.** Pick a seeded policy, set an amount, submit.
   A claim ≤ ₹1,00,000 with a clean fraud signal **auto-approves and settles** within a few
   seconds (watch the saga progress on the claim detail page).
2. **My Claims → claim detail.** See the **lifecycle stepper** (Filed → Triaged → Approved →
   Paid), the fraud risk score, and the full status history timeline.
3. **File a risky claim.** Use a large amount (e.g. ₹4,00,000) or words like *“stolen, total
   loss”* in the description → the claim is **referred / flagged**.
4. **Adjuster Workbench → Fraud & Risk Alerts.** The flagged claim appears as a case with its
   risk meter, severity, duplicate flag, and **SHAP-style explainability**. Confirm fraud
   (rejects the claim) or mark legit (approves it) — the decision flows back to the claim.

This exercises the four UI screens in the brief: *File a Claim*, *Claim Details & Lifecycle
Tracking*, *Recent Claims Dashboard*, *Fraud & Risk Alerts*.

---

## How the design maps to the code

| LLD concept | Where it lives |
|---|---|
| Transactional outbox (A.1.5 / A.1.8) | `ClaimsDbContext.SaveChangesAsync` drains domain events → `OutboxEvent`; `OutboxDispatcher` tails + publishes |
| Adjudication Durable saga (A.1.3.2) | `AdjudicationService` — fraud score → triage → decide → settle (in-process stand-in for Durable Functions) |
| Idempotency-Key → Redis (API spec §2) | `IIdempotencyStore` (`InMemoryIdempotencyStore`); enforced in `POST /v1/claims` and `POST /v1/payments` |
| Fail-open fraud (A.2.6 / FR-050) | `FraudScoringClient` returns `decision=refer` on any Fraud outage |
| Explainable scoring (A.2.3.1) | `RiskScorer` returns 0..1 score + SHAP top-N contributions |
| Once-and-only-once settlement (A.6 / §3.1.7) | `Payments` keys captures by `Idempotency-Key`; replays return the original |
| RFC-7807 error catalogue (A.1.6) | `Error` + `ProblemDetailsExtensions`; codes `CLM-*`, `FR-*`, `DOC-*`, `PAY-*` |
| State machine (A.1.2) | `Claim` aggregate enforces all transitions; `CLM-021` on violation |

---

## Local ↔ Azure — config-switchable adapters

Every external dependency has **both** a local implementation and an **Azure adapter behind the
same interface**, selected at runtime by configuration. With no Azure config the platform runs
fully offline; inject the endpoints (the Bicep in [`infra/`](infra/) does this automatically) and
the Azure adapters activate — **no code change**.

| Concern | Azure adapter (activates when configured) | Local default |
|---|---|---|
| Persistence | EF Core → **Azure SQL / SQL MI** (`ConnectionStrings:*`) | EF Core **InMemory** |
| Messaging | **Service Bus** (`Azure:ServiceBus:FullyQualifiedNamespace`) | in-process bus + outbox dispatcher |
| Idempotency cache | **Azure Cache for Redis** (`Azure:Redis:ConnectionString`) | in-memory store |
| Document metadata | **Cosmos DB** (`Azure:Cosmos:Endpoint`) | in-memory dictionary |
| Document upload | **Blob user-delegation SAS** (`Azure:Storage:BlobServiceUri`) | loopback “staging PUT” |
| Secrets / config | **Key Vault** (`Azure:KeyVault:Uri`) | appsettings / env |
| Telemetry | **Azure Monitor / App Insights** (`APPLICATIONINSIGHTS_CONNECTION_STRING`) | console logging |
| Fraud model | **Azure ML endpoint** (`Fraud:Aml:Endpoint`) | deterministic `RiskScorer` heuristic + SHAP |
| Identity | **Managed Identity** (`DefaultAzureCredential`, `AZURE_CLIENT_ID`) | developer creds / none |
| Payments | simulated capture + SHA-256 receipt hash (Razorpay/HDFC + Confidential Ledger in prod) | same |
| Edge / API plane | Front Door + WAF (`infra/edge.bicep`) → YARP gateway (APIM stand-in) | YARP gateway |
| Compute | Azure **Container Apps** (AKS stand-in) | local Kestrel processes |

**Deploying to Azure:** see [`infra/README.md`](infra/README.md) — `./infra/deploy.ps1` provisions the
data + platform tier, builds/pushes images to ACR, and deploys the services to Container Apps with
Managed Identity + RBAC.

---

## Repository layout

```
insurtech-platform/
├── backend/
│   ├── InsurTech.sln
│   ├── Directory.Build.props
│   ├── src/
│   │   ├── BuildingBlocks/InsurTech.BuildingBlocks/   shared: results, outbox, idempotency, bus, host defaults
│   │   ├── Gateway/InsurTech.Gateway/                 YARP (APIM stand-in)
│   │   └── Services/
│   │       ├── Policy/Policy.Api/
│   │       ├── Claims/{Claims.Domain, Claims.Application, Claims.Infrastructure, Claims.Api}
│   │       ├── Fraud/Fraud.Api/
│   │       ├── Documents/DocMgmt.Api/
│   │       ├── Payments/Payments.Api/
│   │       └── Partner/Partner.Api/
│   └── tests/Claims.UnitTests/
├── frontend/                                          React + Vite MFE shell
└── scripts/                                           run / build / test PowerShell helpers
```
