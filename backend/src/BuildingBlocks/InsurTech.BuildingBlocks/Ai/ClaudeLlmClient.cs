using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace InsurTech.BuildingBlocks.Ai;

/// <summary>
/// Anthropic Claude implementation of <see cref="ILlmClient"/> (substitutes for Azure OpenAI in
/// the AI/ML tier). Uses the Messages API with prompt caching on the system prompt. Activated
/// when <c>Llm:Provider=Claude</c> and an API key is configured.
/// </summary>
public sealed class ClaudeLlmClient(HttpClient http, string apiKey, string model, ILogger<ClaudeLlmClient> logger) : ILlmClient
{
    public bool Enabled => true;
    public string ModelName => model;

    public async Task<string> CompleteAsync(string system, string user, int maxTokens = 512, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                model,
                max_tokens = maxTokens,
                // System prompt sent as a cacheable block (5-min prompt cache) — cuts cost on repeats.
                system = new object[] { new { type = "text", text = system, cache_control = new { type = "ephemeral" } } },
                messages = new object[] { new { role = "user", content = user } }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
            return text ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Claude completion failed; caller will fall back to rule-based analysis");
            return string.Empty; // graceful degrade
        }
    }
}
