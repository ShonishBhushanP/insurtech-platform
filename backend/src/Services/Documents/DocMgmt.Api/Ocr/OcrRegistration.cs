namespace InsurTech.DocMgmt.Api.Ocr;

public static class OcrRegistration
{
    /// <summary>
    /// Registers OCR (reads the real uploaded bytes), in priority order:
    /// 1. Azure Document Intelligence — <c>Azure:DocIntel:Endpoint</c> + <c>Azure:DocIntel:Key</c>.
    /// 2. Claude vision — <c>Llm:Provider=Claude</c> + <c>Llm:Anthropic:ApiKey</c> (multimodal Messages API).
    /// 3. Local stub — derives byte attributes + canned, type-appropriate values (no real read).
    /// </summary>
    public static IServiceCollection AddDocumentExtraction(this IServiceCollection services, IConfiguration config)
    {
        var endpoint = config["Azure:DocIntel:Endpoint"];
        var key = config["Azure:DocIntel:Key"];

        var llmProvider = config["Llm:Provider"];
        var anthropicKey = config["Llm:Anthropic:ApiKey"];

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key))
        {
            var model = config["Azure:DocIntel:Model"] ?? "prebuilt-layout";
            services.AddHttpClient("docintel", c =>
            {
                c.BaseAddress = new Uri(endpoint);
                c.Timeout = TimeSpan.FromSeconds(60);
            });
            services.AddSingleton<IDocumentExtraction>(sp => new AzureDocumentIntelligenceClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("docintel"), key, model));
        }
        else if (string.Equals(llmProvider, "Claude", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(anthropicKey))
        {
            var model = config["Llm:Anthropic:VisionModel"] ?? config["Llm:Anthropic:Model"] ?? "claude-sonnet-4-6";
            services.AddHttpClient("anthropic-vision", c =>
            {
                c.BaseAddress = new Uri("https://api.anthropic.com");
                c.Timeout = TimeSpan.FromSeconds(60);
            });
            services.AddSingleton<IDocumentExtraction>(sp => new ClaudeVisionExtraction(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic-vision"),
                anthropicKey!, model,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClaudeVisionExtraction>>()));
        }
        else
        {
            services.AddSingleton<IDocumentExtraction, LocalExtractionStub>();
        }
        return services;
    }
}
