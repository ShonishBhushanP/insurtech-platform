namespace InsurTech.Fraud.Api.Scoring;

public record ShapContribution(string Feature, double Value);
public record ScoreOutput(double Score, IReadOnlyList<ShapContribution> ShapTopN);

/// <summary>
/// Deterministic heuristic stand-in for the AML fraud model (LLD A.2.3.1). Produces a 0..1
/// risk score plus SHAP-style feature contributions for explainability (IRDAI Art. 22 — every
/// adverse AI decision must be explainable). Swappable for a real Azure ML endpoint behind
/// the same contract.
/// </summary>
public sealed class RiskScorer
{
    private static readonly string[] SuspiciousKeywords =
        { "stolen", "total loss", "write-off", "fire", "flood", "cash", "missing", "untraceable", "unwitnessed" };

    public const string ModelVersion = "heuristic-v1.2";

    public ScoreOutput Score(string claimType, decimal claimedAmount, string summary, bool duplicateSuspected)
    {
        var contributions = new List<ShapContribution>();

        // 1) Amount risk — larger claims carry more risk, normalized and capped.
        var amountRisk = Math.Min(0.35, (double)claimedAmount / 500_000d * 0.35);
        contributions.Add(new("claimed_amount", Math.Round(amountRisk, 3)));

        // 2) Claim-type base rate
        var typeRisk = claimType.ToLowerInvariant() switch
        {
            "life" => 0.25, "health" => 0.20, "travel" => 0.18, "property" => 0.10, _ => 0.05
        };
        contributions.Add(new("claim_type_base_rate", typeRisk));

        // 3) Narrative signals
        var text = summary?.ToLowerInvariant() ?? string.Empty;
        var hits = SuspiciousKeywords.Count(k => text.Contains(k));
        var narrativeRisk = Math.Min(0.30, hits * 0.12);
        if (narrativeRisk > 0) contributions.Add(new("narrative_keywords", Math.Round(narrativeRisk, 3)));

        // 4) Duplicate / velocity signal
        var duplicateRisk = duplicateSuspected ? 0.30 : 0.0;
        if (duplicateRisk > 0) contributions.Add(new("duplicate_velocity", duplicateRisk));

        // 5) Small deterministic model noise (stable per summary)
        var jitter = ((Math.Abs((summary ?? "x").GetHashCode()) % 100) / 100d - 0.5) * 0.06;
        contributions.Add(new("model_residual", Math.Round(jitter, 3)));

        var raw = amountRisk + typeRisk + narrativeRisk + duplicateRisk + jitter;
        var score = Math.Clamp(raw, 0.01, 0.99);

        var topN = contributions.OrderByDescending(c => Math.Abs(c.Value)).Take(5).ToList();
        return new ScoreOutput(Math.Round(score, 4), topN);
    }

    public static string Decide(double score, double blockAt, double referAt) =>
        score >= blockAt ? "block" : score >= referAt ? "refer" : "allow";
}
