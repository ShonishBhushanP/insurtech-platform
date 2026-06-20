using Microsoft.ML;
using Microsoft.ML.Data;

namespace InsurTech.Fraud.Api.Scoring;

/// <summary>
/// Custom fraud-detection model trained with ML.NET (FastTree gradient-boosted trees) — the
/// in-process realisation of the diagram's "Azure ML — Custom Fraud Detection Model". On first
/// use it trains on a synthetic labelled dataset, persists <c>fraud-model.zip</c>, and serves
/// predictions. Same score/explainability contract as the heuristic and the Azure ML endpoint,
/// so it's selectable behind <see cref="IFraudScoringEngine"/>.
/// </summary>
public sealed class FraudModel
{
    public const string Version = "mlnet-fasttree-v1";

    private static readonly string[] FeatureNames = { "claimed_amount", "claim_type_rate", "narrative_keywords", "duplicate_velocity" };
    private static readonly string[] Keywords =
        { "stolen", "total loss", "write-off", "fire", "flood", "cash", "missing", "untraceable", "unwitnessed" };

    private readonly MLContext _ml = new(seed: 42);
    private readonly PredictionEngine<ClaimSample, ClaimScore> _engine;
    private readonly object _lock = new();

    public FraudModel(string modelPath, ILogger<FraudModel> logger)
    {
        ITransformer model;
        DataViewSchema schema;
        if (File.Exists(modelPath))
        {
            model = _ml.Model.Load(modelPath, out schema);
            logger.LogInformation("Fraud model loaded from {Path}", modelPath);
        }
        else
        {
            var data = _ml.Data.LoadFromEnumerable(GenerateTrainingData(4000));
            var pipeline = _ml.Transforms
                .Concatenate("Features", nameof(ClaimSample.AmountNorm), nameof(ClaimSample.ClaimTypeRate),
                                          nameof(ClaimSample.NarrativeRisk), nameof(ClaimSample.Duplicate))
                .Append(_ml.BinaryClassification.Trainers.FastTree(
                    labelColumnName: nameof(ClaimSample.Label), featureColumnName: "Features",
                    numberOfLeaves: 24, numberOfTrees: 120, minimumExampleCountPerLeaf: 10));
            model = pipeline.Fit(data);
            schema = data.Schema;
            try { _ml.Model.Save(model, schema, modelPath); logger.LogInformation("Fraud model trained + saved to {Path}", modelPath); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not persist fraud model (read-only fs?); using in-memory model"); }
        }
        _engine = _ml.Model.CreatePredictionEngine<ClaimSample, ClaimScore>(model, schema);
    }

    /// <summary>Predicts fraud probability (0..1) + per-feature contribution values for explainability.</summary>
    public (double Score, IReadOnlyList<(string Feature, double Value)> Contributions) Predict(
        string claimType, decimal claimedAmount, string summary, bool duplicate)
    {
        var sample = BuildSample(claimType, claimedAmount, summary, duplicate);
        ClaimScore p;
        lock (_lock) { p = _engine.Predict(sample); } // PredictionEngine isn't thread-safe

        var contributions = new (string, double)[]
        {
            (FeatureNames[0], sample.AmountNorm),
            (FeatureNames[1], sample.ClaimTypeRate),
            (FeatureNames[2], Math.Min(0.30, sample.NarrativeRisk * 0.12)),
            (FeatureNames[3], sample.Duplicate * 0.30),
        };
        return (Math.Clamp(p.Probability, 0f, 1f), contributions);
    }

    private static ClaimSample BuildSample(string claimType, decimal amount, string summary, bool duplicate)
    {
        var text = (summary ?? string.Empty).ToLowerInvariant();
        return new ClaimSample
        {
            AmountNorm = (float)Math.Min((double)amount / 500_000d, 1.5),
            ClaimTypeRate = TypeRate(claimType),
            NarrativeRisk = Keywords.Count(k => text.Contains(k)),
            Duplicate = duplicate ? 1f : 0f,
        };
    }

    private static float TypeRate(string claimType) => claimType.ToLowerInvariant() switch
    {
        "life" => 0.25f, "health" => 0.20f, "travel" => 0.18f, "property" => 0.10f, _ => 0.05f
    };

    // Synthetic labelled data: fraud probability rises with amount, suspicious narrative,
    // claim-type base rate, and duplicates — with noise so the tree learns a real boundary.
    private static IEnumerable<ClaimSample> GenerateTrainingData(int n)
    {
        var rng = new Random(42);
        var types = new[] { "Motor", "Health", "Property", "Travel", "Life" };
        for (var i = 0; i < n; i++)
        {
            var amount = rng.NextDouble() * 600_000;
            var type = types[rng.Next(types.Length)];
            var hits = rng.Next(0, 4);
            var dup = rng.NextDouble() < 0.12;
            var amountNorm = Math.Min(amount / 500_000d, 1.5);
            var latentRisk = amountNorm * 0.30 + TypeRate(type) + hits * 0.12 + (dup ? 0.30 : 0.0);
            var isFraud = rng.NextDouble() < Math.Clamp(latentRisk, 0.02, 0.95);
            yield return new ClaimSample
            {
                AmountNorm = (float)amountNorm,
                ClaimTypeRate = TypeRate(type),
                NarrativeRisk = hits,
                Duplicate = dup ? 1f : 0f,
                Label = isFraud,
            };
        }
    }
}

public sealed class ClaimSample
{
    public float AmountNorm { get; set; }
    public float ClaimTypeRate { get; set; }
    public float NarrativeRisk { get; set; }
    public float Duplicate { get; set; }
    public bool Label { get; set; }
}

public sealed class ClaimScore
{
    public bool PredictedLabel { get; set; }
    public float Probability { get; set; }
}
