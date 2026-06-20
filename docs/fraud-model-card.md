# Model Card — InsurTech Custom Fraud Detection Model

**Model id:** `mlnet-fasttree-v1`
**Owner:** InsurTech Architecture Office · **Status:** demo / vertical-slice
**Framework:** ML.NET 3.0 · **Algorithm:** FastTree (gradient-boosted decision trees), binary classification

## Purpose
Produce a fraud **risk score (0..1)** and an **explainable** breakdown for each incoming claim, used
by the Claims adjudication saga to decide *allow / refer / block* and to open investigation cases.
This is the in-process realisation of the deployment diagram's **Azure ML — Custom Fraud Detection
Model**. It is selectable behind `IFraudScoringEngine` alongside the heuristic and an Azure ML
managed endpoint, so it can be swapped for a cloud-hosted model with no code change.

## Intended use / out of scope
- **Intended:** decision-support signal for adjudication + adjuster review. Every adverse outcome
  is human-reviewable (IRDAI Art. 22 — explainability required).
- **Out of scope:** it does **not** auto-reject claims on its own; high scores route to SIU/underwriter.

## Inputs (features)
| Feature | Meaning |
|---|---|
| `AmountNorm` | claimed amount normalised (`amount / 500,000`, capped 1.5) |
| `ClaimTypeRate` | per-product base rate (Life .25 · Health .20 · Travel .18 · Property .10 · Motor .05) |
| `NarrativeRisk` | count of suspicious keywords in the description (stolen, total loss, cash, …) |
| `Duplicate` | 1 if a same-policy/same-amount claim was scored in the last 10 min (velocity signal) |

## Output
- `Probability` (0..1) → the risk score. Decision bands (configurable): `≥0.85 block`, `≥0.55 refer`, else `allow`.
- SHAP-style per-feature contributions for the adjuster UI.

## Training data
Synthetic, generated deterministically (`seed=42`, 4,000 rows). Fraud likelihood is a noisy function
of the four features (higher amount / suspicious narrative / risky product / duplicates ⇒ higher fraud
probability), so the tree learns a genuine, non-linear boundary rather than the coded heuristic weights.
*No real customer data is used.*

## Training & lifecycle
- Trained on first run; persisted to `fraud-model.zip` and reloaded thereafter (retrains if absent —
  e.g. after a container restart on ephemeral storage).
- **Production path:** replace synthetic data with labelled historical claims, train/register in Azure
  ML, and expose a managed online endpoint; point `Fraud:Aml:Endpoint` at it. Azure ML then provides
  versioning, **blue/green model swap**, champion/challenger, drift monitoring, and scheduled retraining
  (LLD §A.2.3.3) — none of which the in-process model offers.

## Limitations
- Trained on synthetic data → metrics are illustrative, not production-validated.
- Explainability shows feature-value contributions (full per-prediction SHAP/FCC or the Azure ML
  endpoint's SHAP output is the production upgrade).
- Model is coupled to the app (update ⇒ redeploy); the Azure ML path decouples it.

## Configuration
| Setting | Effect |
|---|---|
| *(default)* | ML.NET model (`mlnet-fasttree-v1`), heuristic fallback on error |
| `Fraud:ScoringEngine=Heuristic` | use the rule-based `RiskScorer` instead |
| `Fraud:Aml:Endpoint` (+ `Key`) | call an Azure ML managed endpoint instead |
| `Fraud:Model:Path` | override the model file location |
