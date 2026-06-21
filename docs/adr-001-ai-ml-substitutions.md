# ADR-001 — AI/ML service substitutions (Claude + ML.NET in place of Azure cognitive services)

**Status:** Accepted · **Context:** Phase 3 execution on an Azure *learner* sandbox
**Decision owner:** platform team · **Applies to:** the AI/ML tier of the deployment diagram

---

## 1. Context

The HLD/LLD deployment diagram specifies an **Azure-native AI/ML tier**:

| Capability | Architecture's intended Azure service |
|---|---|
| OCR / form recognition | Azure AI **Document Intelligence** |
| Custom fraud model | **Azure Machine Learning** managed online endpoint |
| Fraud analysis / summarization | **Azure OpenAI** |

The platform had to be **built and deployed on a learner-grade Azure subscription** ("Learner
Account") with hard constraints. We kept every other tier Azure-native (Azure SQL, Container Apps,
Static Web Apps, ACR, Application Insights/Log Analytics, plus config-gated adapters for Blob,
Cosmos, Service Bus, Key Vault). For the **three AI/ML cognitive services above**, we deliberately
ran **functionally-equivalent substitutes** and kept the Azure-native paths as switchable adapters.

This ADR records **why**.

---

## 2. The binding constraints

These are properties of the sandbox, not preferences:

1. **Azure OpenAI is gated behind a separate access approval.** It is a Limited Access service —
   you must apply with a business justification and be granted access on the subscription. A
   learner sandbox cannot be granted it, so **Azure OpenAI was simply not provisionable**.
2. **No RBAC / Managed Identity.** The account has **Contributor but not
   `Microsoft.Authorization/roleAssignments/write`**, so we cannot create the role assignments that
   Managed-Identity access to Document Intelligence / Azure ML / Azure OpenAI would normally use.
   Access would have to be **key-based**, which also conflicts with the secure-by-default design.
3. **Standing cost is the budget killer.** The sandbox has a small, shared budget and we were
   already trimming it (deleted orphan storage/ACR, debated whether Log Analytics was worth it).
   An **Azure ML managed online endpoint runs a dedicated compute instance 24/7** — it bills by the
   hour **even when idle**, which is the worst possible shape for a demo that's used intermittently.
4. **~15-minute credential expiry + corporate proxy (Zscaler).** Provisioning-heavy services with
   long setup (model deployment, endpoint warm-up) repeatedly hit token expiry, and data-plane
   calls were blocked on the corporate network — adding operational drag with no architectural
   payoff for a demo.
5. **Scale-to-zero economics.** The app itself runs on Container Apps scaled to (near) zero between
   demos. Pairing that with always-on AI endpoints would defeat the cost model.

---

## 3. The decisions (per capability)

### 3.1 OCR — Claude vision instead of Azure Document Intelligence

- **Substitute:** `ClaudeVisionExtraction` — sends the uploaded image/PDF to Claude's multimodal
  Messages API and parses the fields it reads.
- **Why:** Document Intelligence is a **per-page paid resource** (S0) requiring a provisioned
  account + key. For a demo with a handful of documents, a **pay-per-call** LLM with **no standing
  cost** and **no provisioning** is cheaper and simpler, and Claude reads free-form documents
  (death certificate, medical report, repair quote) without per-document model training.
- **Cost shape:** Document Intelligence ≈ a few ₹/USD-cents **per page** on a provisioned account;
  Claude vision ≈ **fractions of a cent per document**, billed only when a document is uploaded,
  **₹0 when idle.**
- **Trade-off:** Document Intelligence returns structured key/value pairs with confidence scores and
  is purpose-built/SLA-backed for forms; the LLM is more flexible but without per-field confidence.
  Acceptable for a demo; the Azure path is one config switch away for production.

### 3.2 Fraud model — ML.NET in-process instead of an Azure ML endpoint

- **Substitute:** `MLNetScoringEngine` — a FastTree gradient-boosted classifier trained in-process
  (4,000 synthetic rows, persisted to `fraud-model.zip`), served inside the Fraud container.
- **Why:** An Azure ML **managed online endpoint is the single biggest standing cost** in the AI
  tier (a dedicated VM-backed compute, billed hourly 24/7) and needs RBAC we don't have. ML.NET
  gives a **real trained model** (same score + SHAP-style explainability contract) with
  **zero marginal cost** and **zero extra infrastructure** — it runs in the service that already
  exists and scales to zero with it.
- **Cost shape:** Azure ML endpoint ≈ **standing hourly compute cost** regardless of traffic;
  ML.NET ≈ **₹0** (CPU already paid for as part of the container).
- **Trade-off:** No managed model registry / no-code retraining / autoscaling inference. For our
  volume the in-process model is more than sufficient; the `AzureMlScoringEngine` adapter switches
  to a real endpoint via `Fraud:Aml:Endpoint` with no code change.

### 3.3 Fraud analysis (and OCR LLM) — Anthropic Claude instead of Azure OpenAI

- **Substitute:** `ClaudeLlmClient` behind the `ILlmClient` abstraction (fraud briefing) and the
  Claude vision engine (OCR).
- **Why:** **Azure OpenAI was not available** on the sandbox (§2.1). Claude provides the same
  capability (structured extraction, summarization) through the same `ILlmClient` seam, with a
  pay-per-token model and no subscription gating.
- **Cost shape:** both are per-token; Claude needed **no access application** and **no Azure
  resource**, so it was the only viable path under the deadline.
- **Trade-off:** Not running on the Azure OpenAI resource the diagram names. Mitigated by the
  `ILlmClient` abstraction — an `AzureOpenAiLlmClient` can be added later with no caller changes.

---

## 4. Why this is still architecture-faithful

The substitutions are **swaps behind stable interfaces**, not shortcuts:

- `IDocumentExtraction`, `IFraudScoringEngine`, `ILlmClient` are the seams. Each has **both** a
  local/substitute implementation **and** an Azure-native implementation selected purely by config.
- Every engine is **fail-open / graceful-degrade** — a missing key or provider error falls back to
  the local engine; the claim/document pipeline never breaks.
- Moving to the Azure-native tier in production is a **configuration change** (set the endpoint/key),
  except Azure OpenAI which needs a small `ILlmClient` adapter — the call sites are unchanged.

This demonstrates the architectural goal: the **design is Azure-native and the deployment is
constraint-appropriate**, with a documented, low-risk path to full fidelity.

## 5. Switch-back reference (production / funded subscription)

| Capability | Set config | Engine that activates |
|---|---|---|
| OCR | `Azure:DocIntel:Endpoint`, `Azure:DocIntel:Key` | `AzureDocumentIntelligenceClient` |
| Fraud score | `Fraud:Aml:Endpoint`, `Fraud:Aml:Key` | `AzureMlScoringEngine` |
| LLM tier | (add `AzureOpenAiLlmClient`) + endpoint/key/deployment | Azure OpenAI via `ILlmClient` |

Also revisit when funded: Managed Identity + RBAC instead of keys (§2.2), and APIM/AKS instead of
the YARP gateway / Container Apps stand-ins.

See also: `docs/ai-pipelines.md` (runtime flows), `docs/fraud-model-card.md`,
`docs/fraud-model-training-explained.md`.
