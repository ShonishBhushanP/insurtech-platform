# How the Custom Fraud Model Is Trained (ML.NET)

A plain-English walkthrough of how `mlnet-fasttree-v1` is trained, mapped to the code in
[`backend/src/Services/Fraud/Fraud.Api/Scoring/FraudModel.cs`](../backend/src/Services/Fraud/Fraud.Api/Scoring/FraudModel.cs).

---

## Where the model sits
The model lives **inside the Fraud service**, behind the `IFraudScoringEngine` interface, so the
rest of the platform never depends on which engine scores a claim:

```
Claim filed (Claims) ─► adjudication saga ─► POST /v1/fraud/score (Fraud.Api)
                                                     │
                                          IFraudScoringEngine  ◄── selected by config
                                          ├─ MLNetScoringEngine   (default — the custom model)
                                          ├─ HeuristicScoringEngine (RiskScorer rules — fallback)
                                          └─ AzureMlScoringEngine   (calls an Azure ML endpoint)
                                                     │
                                          FraudModel (ML.NET FastTree)  ─► score 0..1
                                                     │
                                          decision bands ─► allow / refer / block ─► open case
```

All engines return the same shape (`ScoreOutput` = score + SHAP contributions), so they're
interchangeable with no code/UI change.

## When training runs
On first startup the Fraud service resolves `FraudModel`. If `fraud-model.zip` **does not exist**,
it trains (~1–2 s), saves the file, and reloads it on subsequent runs. So training happens once per
fresh container (the container filesystem is ephemeral, so a restart retrains from the same seed →
an identical model).

Training is four steps: **synthesize labelled data → build a pipeline → `Fit` → calibrate & save.**

---

## Step 1 — Create a labelled training set (`GenerateTrainingData`)
ML.NET needs rows of *features* + a known *answer* (label `IsFraud = true/false`). With no real
claims data, we synthesize 4,000 rows using a **seeded** RNG (`new Random(42)` → reproducible model):

For each synthetic claim:
```
amount      = random 0..600,000
type        = random of {Motor, Health, Property, Travel, Life}
hits        = random 0..3      (suspicious keyword count)
duplicate   = random, ~12% true
amountNorm  = min(amount/500,000, 1.5)

// hidden "truth" function — how fraudy this profile really is
latentRisk  = amountNorm*0.30 + typeRate(type) + hits*0.12 + (duplicate ? 0.30 : 0)

// LABEL it probabilistically (not a hard cutoff):
IsFraud     = random() < clamp(latentRisk, 0.02, 0.95)
```

The **last line** is the key: the label is *probabilistic*, not a clean threshold. A risky-looking
claim is *likely* fraud but not always; a clean one is *usually* legit but occasionally fraud. That
deliberate noise forces the model to learn a real statistical boundary instead of memorizing a
trivial rule.

**Worked examples**
- Health (typeRate 0.20), amount 480k (amountNorm ≈ 0.96 → ×0.30 = 0.29), 3 keywords (0.36),
  duplicate (0.30) → latentRisk ≈ 1.15 → clamped 0.95 → ~95% chance labelled **fraud**.
- Motor ₹40k, clean → latentRisk ≈ 0.07 → ~7% chance fraud.

Rows are loaded into an ML.NET `IDataView` via `LoadFromEnumerable(...)`.

## Step 2 — Build the learning pipeline
```csharp
var pipeline = _ml.Transforms
    .Concatenate("Features", AmountNorm, ClaimTypeRate, NarrativeRisk, Duplicate)   // featurize
    .Append(_ml.BinaryClassification.Trainers.FastTree(                              // learner
        labelColumnName: "Label", featureColumnName: "Features",
        numberOfLeaves: 24, numberOfTrees: 120, minimumExampleCountPerLeaf: 10));
```
- **`Concatenate`** packs the 4 feature columns into a single numeric vector `Features`.
- **`FastTree`** is the learning algorithm — gradient-boosted decision trees.

## Step 3 — `Fit` (the actual learning)
```csharp
var model = pipeline.Fit(data);
```
**FastTree = gradient boosting:** it builds **120 small decision trees in sequence**, each new tree
correcting the errors of the ones before it. Each tree splits on feature thresholds (e.g.
"amountNorm > 0.7?", "duplicate = 1?", "keywords ≥ 2?"), up to **24 leaves** per tree, requiring
**≥10 examples per leaf** (guards against overfitting noise). The thresholds and tree shapes are
**learned from the 4,000 rows** — not coded by us. That's the difference from the heuristic.

This also captures **non-linear interactions** a flat weighted sum can't — e.g. "high amount *and* a
duplicate" is far riskier than either feature alone.

## Step 4 — Calibrate to a probability, then save
The binary FastTree trainer auto-applies **Platt calibration** (a sigmoid), converting the raw
ensemble score into a true **`Probability` in 0..1** — the fraud score you see (0.90, 0.27, …). Then:
```csharp
_ml.Model.Save(model, schema, "fraud-model.zip");
```
The trees + calibrator + schema are serialized to the zip and reloaded on later runs.

---

## Inference (scoring a real claim)
1. `BuildSample(...)` converts the claim into the 4 features:
   - `AmountNorm` = amount ÷ 500,000 (capped 1.5)
   - `ClaimTypeRate` = base rate by product (Motor .05 · Property .10 · Travel .18 · Health .20 · Life .25)
   - `NarrativeRisk` = count of suspicious keywords (stolen, total loss, cash, …)
   - `Duplicate` = 1 if a same-policy/same-amount claim was scored in the last 10 min
2. A `PredictionEngine<ClaimSample, ClaimScore>` (wrapped in a `lock`, since it isn't thread-safe)
   runs the features down all 120 trees → calibrated `Probability`.
3. `RiskScorer.Decide(score, 0.85, 0.55)` maps it to **allow / refer / block**; non-`allow` opens a
   fraud case for the adjuster.

## What "trained" means here
The model has **learned from data how the four features combine to predict fraud**, including
non-linear interactions — rather than applying hand-coded weights. That is why it can be A/B-tested
against the heuristic or graduated to a cloud model with no code change.

## How production training differs
Same pipeline, three changes:
1. **Real labelled data** — historical claims with confirmed fraud/legit outcomes (fed by the
   Audit / closed-loop labelling), instead of synthetic rows.
2. **Train/test split + metrics** — measure AUC, precision/recall on held-out data before shipping.
3. **Trained in Azure ML**, registered + versioned, served from a managed online endpoint
   (`Fraud:Aml:Endpoint`) with blue/green rollout, champion/challenger, and drift monitoring —
   rather than trained in-process at startup.

## Configuration switch
| Setting | Engine used |
|---|---|
| *(default)* | ML.NET model (`mlnet-fasttree-v1`), heuristic fallback on error |
| `Fraud:ScoringEngine=Heuristic` | rule-based `RiskScorer` (`heuristic-v1.2`) |
| `Fraud:Aml:Endpoint` (+ `Key`) | Azure ML managed endpoint |
| `Fraud:Model:Path` | override the model file location |

See also: [`fraud-model-card.md`](fraud-model-card.md).
