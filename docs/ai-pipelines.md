# AI/ML Pipelines — OCR & Fraud (how they run today)

This documents the two AI/ML flows in the running platform: **document OCR / form-recognition**
and **fraud detection**. Both follow the same principle as the rest of the system — a
**config-switchable adapter** with a graceful local fallback — so the platform runs end-to-end with
no external dependencies, and "lights up" the real engine when a key/endpoint is configured.

```
                         IDocumentExtraction                 IFraudScoringEngine + ILlmClient
  upload a document ───────────►  OCR  ─┐               claim filed ──────►  fraud score + analysis
                                        │
   1. Azure Document Intelligence       │  priority      1. Azure ML endpoint        (score)
   2. Claude vision   ◄── ACTIVE NOW    │  order         2. ML.NET model ◄ ACTIVE    (score)
   3. Local stub      (fallback)        ┘                3. Heuristic   (fallback)   (score)
                                                         + Claude / rule-based       (analysis)
```

---

## 1. Document OCR / form-recognition

**Where:** Document Management service (`DocMgmt.Api`). Interface: `IDocumentExtraction`.
**Engine selection** (`Ocr/OcrRegistration.cs`), first match wins:

| Priority | Engine | Activated by | Reads real bytes? |
|---|---|---|---|
| 1 | `AzureDocumentIntelligenceClient` | `Azure:DocIntel:Endpoint` + `Azure:DocIntel:Key` | yes (REST, prebuilt model) |
| 2 | **`ClaudeVisionExtraction`** ← **live now** | `Llm:Provider=Claude` + `Llm:Anthropic:ApiKey` | yes (multimodal LLM) |
| 3 | `LocalExtractionStub` | (default, no config) | no — byte attributes + canned values |

### Runtime flow (what happens on every upload)

```
UI: File a Claim, attach evidence
  │
  ├─ POST /v1/documents/upload-url        → validates name/MIME/size, allocates documentId,
  │                                          stores metadata (Status=PendingUpload)
  │
  └─ PUT  /v1/documents/{id}/_staging-put  (the file bytes)
        │
        ├─ store bytes in the content store (Blob immutable container in Azure)
        ├─ malware scan (clean — stub)
        ├─ OCR  ▶ IDocumentExtraction.ExtractAsync(fileName, mime, bytes, …)
        │        │
        │        │  ClaudeVisionExtraction (current engine):
        │        │   1. derive trustworthy byte attributes locally
        │        │      (detectedFormat, imageDimensions, fileSize, documentTypeVerified)
        │        │   2. base64-encode the bytes → Claude Messages API
        │        │        • image/*  → "image" content block
        │        │        • application/pdf → "document" content block
        │        │      prompt: "read this document, return a FLAT JSON object of the
        │        │               fields you can see; always include documentType; do not invent."
        │        │   3. parse Claude's JSON → merge over the byte attributes
        │        │   4. on any failure → LocalExtractionStub (canned) + note
        │        │
        └─ promote to immutable (Status=Promoted), persist ExtractedFields + OcrEngine
```

The adjuster sees the extracted fields + `OCR · <engine>` on the Claim Details page, with an
image/PDF preview. Because the byte attributes (format, dimensions, type-verify) are computed
**locally** and the *content* fields come from Claude, the result is both tamper-evident and
accurate to the actual document (e.g. a death certificate returns the real deceased name,
certificate number, dates — not placeholders).

**Important — this is a server-side call.** The documents container calls `api.anthropic.com`
directly over Azure egress, so the corporate proxy that blocks the browser is irrelevant here.

### Config (documents container app)

```
Llm__Provider          = Claude
Llm__Anthropic__ApiKey = <secretref: anthropic-key>     # Anthropic Console key (sk-ant-…)
Llm__Anthropic__VisionModel = claude-sonnet-4-6         # optional; defaults to this
```

---

## 2. Fraud detection

**Where:** Fraud service (`Fraud.Api`). Two distinct steps with separate adapters:

- **Score** (`IFraudScoringEngine`) — the numeric risk probability. *Always real.*
- **Analysis** (`ILlmClient`) — the human-readable investigator briefing. *LLM or rule-based.*

### 2a. Scoring engine selection (`Scoring/ScoringEngine.cs`)

| Priority | Engine | Activated by | Notes |
|---|---|---|---|
| 1 | `AzureMlScoringEngine` | `Fraud:Aml:Endpoint` (+ key) | posts feature vector to AML `/score`, 2s timeout |
| 2 | **`MLNetScoringEngine`** ← **live now** | (default) | trained ML.NET FastTree model, in-process |
| — | `HeuristicScoringEngine` | `Fraud:ScoringEngine=Heuristic`, **and** the universal fallback | rule-based `RiskScorer` |

The **ML.NET model** (`Scoring/FraudModel.cs`) is a FastTree gradient-boosted binary classifier
(120 trees, 24 leaves). On first start it trains on 4,000 synthetic labelled rows (seed 42),
saves `fraud-model.zip`, then serves predictions. Four features:

| Feature | Derivation |
|---|---|
| `claimed_amount` (AmountNorm) | `min(amount / 500,000, 1.5)` |
| `claim_type_rate` | base fraud rate by type (Life .25, Health .20, Travel .18, Property .10, Motor .05) |
| `narrative_keywords` (NarrativeRisk) | count of risk words in the description (stolen, total loss, fire, cash, unwitnessed, …) |
| `duplicate_velocity` | 1 if same policy+amount scored in the last 10 min, else 0 |

Output: probability 0..1 + the top SHAP-style per-feature contributions (explainability). On any
model error the engine **degrades to the heuristic** — scoring never throws.

### 2b. Runtime flow

```
Claim filed (Claims service) ──► POST /v1/fraud/score {claimId, policyId, type, amount, summary}
   │
   ├─ duplicate/velocity check  (DB: same policy+amount in last 10 min?)
   ├─ engine.ScoreAsync(...)    → score 0..1  (ML.NET; heuristic fallback)
   ├─ Decide(score):  ≥ 0.85 → block   |   ≥ 0.55 → refer   |   else allow
   ├─ persist ScoreRecord
   └─ if decision ≠ allow → open a FraudCase (queued in "Fraud & Risk Alerts")
        returns {score, decision, shapTopN, modelVersion, durationMs}
```

Fail-open (FR-050): if scoring is slow/unavailable, the claim hot path is not blocked.

### 2c. AI fraud analysis (investigator briefing)

`GET /v1/fraud/cases/{id}/analysis` produces a 3–4 sentence briefing:

```
if ILlmClient.Enabled  (Llm:Provider=Claude + key on the FRAUD app):
    Claude Messages API ← system: "fraud investigation assistant…"
                          user:   claim summary + score + duplicate flag + top SHAP features
    → generatedBy = claude-sonnet-4-6
    (empty/failed → rule-based fallback, generatedBy = "rule-based (LLM fallback)")
else:
    deterministic RuleBasedAnalysis(score band + top features + recommended next step)
    → generatedBy = "rule-based"
```

The UI shows the briefing with its `generatedBy` provenance.

---

## 3. Current deployment state (sandbox)

| Capability | Engine running now | To upgrade |
|---|---|---|
| Document OCR | **Claude vision** (`claude-sonnet-4-6`) ✅ | already live; or set `Azure:DocIntel:*` for Document Intelligence |
| Fraud **score** | **ML.NET FastTree** ✅ (real) | set `Fraud:Aml:Endpoint` for an Azure ML endpoint |
| Fraud **analysis** | **rule-based** (no key on `insurtech-fraud`) | set `Llm__Provider=Claude` + `Llm__Anthropic__ApiKey` on the fraud app to use Claude |

> The Anthropic key is currently set on **`insurtech-documents`** (OCR is live). To make the fraud
> *briefing* LLM-generated too, set the same `Llm__Provider` / `Llm__Anthropic__ApiKey` on
> **`insurtech-fraud`**. The fraud *score* is unaffected either way — it's the ML.NET model.

## Cost & security notes
- The Anthropic key is a real, metered Console key — stored as a Container Apps **secret**, never in
  code. Vision OCR on a handful of demo docs costs fractions of a cent each.
- OCR / fraud-analysis LLM calls are **server-side** (container → `api.anthropic.com`); they don't
  touch the browser or the corporate proxy.
- All engines **fail open / degrade gracefully** — a missing key or a provider error never breaks
  the claim or document pipeline; it falls back to the local engine.

**Why substitutes instead of the Azure-native AI services?** See
`docs/adr-001-ai-ml-substitutions.md` — the decision record covering the sandbox + cost
constraints (Azure OpenAI access-gating, Azure ML endpoint standing cost, no RBAC) and the
config switch-back path to full Azure fidelity.

See also: `docs/fraud-model-card.md`, `docs/fraud-model-training-explained.md`,
`infra/OPTION3-SAME-ORIGIN.md`.
