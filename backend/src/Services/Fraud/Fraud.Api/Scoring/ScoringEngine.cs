using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace InsurTech.Fraud.Api.Scoring;

/// <summary>Scoring abstraction so the AML endpoint (Azure) and the local heuristic are swappable.</summary>
public interface IFraudScoringEngine
{
    string ModelVersion { get; }
    Task<ScoreOutput> ScoreAsync(string claimType, decimal claimedAmount, string summary, bool duplicate, CancellationToken ct = default);
}

/// <summary>Local heuristic engine (default) — wraps <see cref="RiskScorer"/>.</summary>
public sealed class HeuristicScoringEngine(RiskScorer scorer) : IFraudScoringEngine
{
    public string ModelVersion => RiskScorer.ModelVersion;
    public Task<ScoreOutput> ScoreAsync(string claimType, decimal claimedAmount, string summary, bool duplicate, CancellationToken ct = default)
        => Task.FromResult(scorer.Score(claimType, claimedAmount, summary, duplicate));
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
        else
        {
            services.AddSingleton<IFraudScoringEngine, HeuristicScoringEngine>();
        }
        return services;
    }
}
