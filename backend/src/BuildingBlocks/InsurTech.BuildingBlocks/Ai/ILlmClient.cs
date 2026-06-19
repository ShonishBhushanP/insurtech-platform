namespace InsurTech.BuildingBlocks.Ai;

/// <summary>
/// Large-language-model abstraction for the platform's "Fraud Analysis / Summarization" and
/// copilot features (deployment diagram: AI/ML — Azure OpenAI). Provider-swappable: Anthropic
/// Claude, Azure OpenAI, or a null stub. <see cref="Enabled"/> is false when no key is configured,
/// so callers fall back to a deterministic, rule-based explanation.
/// </summary>
public interface ILlmClient
{
    bool Enabled { get; }
    string ModelName { get; }

    /// <summary>Single-turn completion. <paramref name="system"/> is cached across calls when supported.</summary>
    Task<string> CompleteAsync(string system, string user, int maxTokens = 512, CancellationToken ct = default);
}

/// <summary>Default when no LLM key is configured — signals callers to use the rule-based path.</summary>
public sealed class NullLlmClient : ILlmClient
{
    public bool Enabled => false;
    public string ModelName => "rule-based";
    public Task<string> CompleteAsync(string system, string user, int maxTokens = 512, CancellationToken ct = default)
        => Task.FromResult(string.Empty);
}
