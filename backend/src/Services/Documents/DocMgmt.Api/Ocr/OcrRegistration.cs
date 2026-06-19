namespace InsurTech.DocMgmt.Api.Ocr;

public static class OcrRegistration
{
    /// <summary>
    /// Registers OCR: Azure Document Intelligence when <c>Azure:DocIntel:Endpoint</c> + key are set,
    /// otherwise the local stub.
    /// </summary>
    public static IServiceCollection AddDocumentExtraction(this IServiceCollection services, IConfiguration config)
    {
        var endpoint = config["Azure:DocIntel:Endpoint"];
        var key = config["Azure:DocIntel:Key"];

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key))
        {
            var model = config["Azure:DocIntel:Model"] ?? "prebuilt-document";
            services.AddHttpClient("docintel", c =>
            {
                c.BaseAddress = new Uri(endpoint);
                c.Timeout = TimeSpan.FromSeconds(60);
            });
            services.AddSingleton<IDocumentExtraction>(sp => new AzureDocumentIntelligenceClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("docintel"), key, model));
        }
        else
        {
            services.AddSingleton<IDocumentExtraction, LocalExtractionStub>();
        }
        return services;
    }
}
