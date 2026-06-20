using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace InsurTech.Fraud.Api.Scoring;

/// <summary>Scoring abstraction so the AML endpoint (Azure) and the local heuristic are swappable.</summary>
public interface IFraudScoringEngine
{
    string ModelVersion { get; }
    Task<ScoreOutput> ScoreAsync(string claimType, decimal claimedAmount, string summary, bool duplicate, CancellationToken ct = default);
}

/// <summary>Local heuristic engine — wraps <see cref="RiskScorer"/>. Used as the universal fallback.</summary>
public sealed class HeuristicScoringEngine(RiskScorer scorer) : IFraudScoringEngine
{
    public string ModelVersion => RiskScorer.ModelVersion;
    public Task<ScoreOutput> ScoreAsync(string claimType, decimal claimedAmount, string summary, bool duplicate, CancellationToken ct = default)
        => Task.FromResult(scorer.Score(claimType, claimedAmount, summary, duplicate));
}

/// <summary>
/// Custom ML.NET model engine (default when no AML endpoint is configured). Scores with the
/// trained FastTree model; on any model error it degrades to the heuristic so scoring never fails.
/// </summary>
public sealed class MLNetScoringEngine(FraudModel model, HeuristicScoringEngine fallback, ILogger<MLNetScoringEngine> logger) : IFraudScoringEngine
{
    public string ModelVersion => FraudModel.Version;

    public async Task<ScoreOutput> ScoreAsync(string claimType, decimal claimedAmount, string summary, bool duplicate, CancellationToken ct = default)
    {
        try
        {
            var (score, contributions) = model.Predict(claimType, claimedAmount, summary, duplicate);
            var shap = contributions
                .OrderByDescending(c => Math.Abs(c.Value))
                .Take(5)
                .Select(c => new ShapContribution(c.Feature, Math.Round(c.Value, 3)))
                .ToList();
            return new ScoreOutput(Math.Round(score, 4), shap);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ML.NET scoring failed; falling back to heuristic");
            return await fallback.ScoreAsync(claimType, claimedAmount, summary, duplicate, ct);
        }
    }
}

/// <summary>
/// Azure Machine Learning managed-endpoint engine (LLD A.2 / TR-05, deployment "AI" tier).
/// Posts the feature vector to the AML scoring URI; on any failure it falls back to the local
/// heuristic so the FNOL hot path never blocks (fail-open, FR-050).
/// </summary>
public sealed class AzureMlScoringEngine(HttpClient http, HeuristicScoringEngine fallback, string modelVersion) : IFraudScoringEngine
{
    private record AmlRequest(string claimType, decimal claimedAmount, string summary, bool duplicate);
    private record AmlResponse(double score, List<ShapContribution>? shap);

    public string ModelVersion => modelVersion;

    public async Task<ScoreOutput> ScoreAsync(string claimType, decimal claimedAmount, string summary, bool duplicate, CancellationToken ct = default)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/score", new AmlRequest(claimType, claimedAmount, summary, duplicate), ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<AmlResponse>(cancellationToken: ct);
            if (body is null) return await fallback.ScoreAsync(claimType, claimedAmount, summary, duplicate, ct);
            return new ScoreOutput(Math.Clamp(body.score, 0, 1), body.shap ?? new());
        }
        catch
        {
            return await fallback.ScoreAsync(claimType, claimedAmount, summary, duplicate, ct);
        }
    }
}

public static class ScoringEngineRegistration
{
    public static IServiceCollection AddScoringEngine(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<HeuristicScoringEngine>();
        var amlEndpoint = config["Fraud:Aml:Endpoint"];

        if (!string.IsNullOrWhiteSpace(amlEndpoint))
        {
            var apiKey = config["Fraud:Aml:Key"];
            var modelVersion = config["Fraud:Aml:ModelVersion"] ?? "aml-endpoint";
            services.AddHttpClient(nameof(AzureMlScoringEngine), c =>
            {
                c.BaseAddress = new Uri(amlEndpoint);
                c.Timeout = TimeSpan.FromMilliseconds(2000);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            });
            services.AddSingleton<IFraudScoringEngine>(sp => new AzureMlScoringEngine(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureMlScoringEngine)),
                sp.GetRequiredService<HeuristicScoringEngine>(),
                modelVersion));
        }
        else if (string.Equals(config["Fraud:ScoringEngine"], "Heuristic", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFraudScoringEngine, HeuristicScoringEngine>();
        }
        else
        {
            // Default: the custom ML.NET model (heuristic fallback inside the engine).
            var modelPath = config["Fraud:Model:Path"] ?? Path.Combine(AppContext.BaseDirectory, "fraud-model.zip");
            services.AddSingleton(sp => new FraudModel(modelPath, sp.GetRequiredService<ILogger<FraudModel>>()));
            services.AddSingleton<IFraudScoringEngine, MLNetScoringEngine>();
        }
        return services;
    }
}
