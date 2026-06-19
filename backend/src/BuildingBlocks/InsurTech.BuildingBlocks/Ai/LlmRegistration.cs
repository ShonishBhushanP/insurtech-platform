using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InsurTech.BuildingBlocks.Ai;

public static class LlmRegistration
{
    /// <summary>
    /// Registers an <see cref="ILlmClient"/>. With <c>Llm:Provider=Claude</c> and
    /// <c>Llm:Anthropic:ApiKey</c> set, uses Claude; otherwise the null stub (rule-based fallback).
    /// </summary>
    public static IServiceCollection AddInsurTechLlm(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Llm:Provider"];
        var apiKey = config["Llm:Anthropic:ApiKey"];

        if (string.Equals(provider, "Claude", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(apiKey))
        {
            var model = config["Llm:Anthropic:Model"] ?? "claude-sonnet-4-6";
            services.AddHttpClient("anthropic", c =>
            {
                c.BaseAddress = new Uri("https://api.anthropic.com");
                c.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddSingleton<ILlmClient>(sp => new ClaudeLlmClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic"),
                apiKey, model,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClaudeLlmClient>>()));
        }
        else
        {
            services.AddSingleton<ILlmClient, NullLlmClient>();
        }
        return services;
    }
}
