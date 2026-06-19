using InsurTech.BuildingBlocks.Persistence;
using InsurTech.Claims.Application.Abstractions;
using InsurTech.Claims.Application.Adjudication;
using InsurTech.Claims.Application.Commands;
using InsurTech.Claims.Infrastructure.ExternalServices;
using InsurTech.Claims.Infrastructure.Outbox;
using InsurTech.Claims.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InsurTech.Claims.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddClaimsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<ClaimsDbContext>(o => o.UseInsurTechStore(config, "ClaimsDb", "claims-db"));
        services.AddScoped<IClaimRepository, ClaimRepository>();

        // Application use cases
        services.AddScoped<FileClaimHandler>();
        services.AddScoped<AdjudicationService>();
        services.AddSingleton(new AdjudicationOptions
        {
            AutoApproveThreshold = config.GetValue("Claims:Adjudication:AutoApproveThreshold", 100_000m)
        });

        // Typed cross-service clients (fail-open). Base addresses come from config (gateway/APIM in prod).
        var fraudBase = config.GetValue("Claims:Services:Fraud", "http://localhost:5103")!;
        var paymentsBase = config.GetValue("Claims:Services:Payments", "http://localhost:5105")!;

        services.AddHttpClient<IFraudScoringClient, FraudScoringClient>(c =>
        {
            c.BaseAddress = new Uri(fraudBase);
            c.Timeout = TimeSpan.FromSeconds(2); // generous local budget; 200ms in Azure (NFR-05)
        });
        services.AddHttpClient<IPaymentClient, PaymentClient>(c =>
        {
            c.BaseAddress = new Uri(paymentsBase);
            c.Timeout = TimeSpan.FromSeconds(5);
        });

        // Adjudication driver: in-process (default) or delegate to Durable Functions (LLD A.1.3.2).
        var mode = config.GetValue("Claims:Adjudication:Mode", "InProcess")!;
        var functionsBase = config.GetValue<string?>("Claims:Adjudication:FunctionsBaseUrl", null);
        if (mode.Equals("DurableFunctions", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(functionsBase))
        {
            services.AddHttpClient<IAdjudicationTrigger, DurableFunctionsAdjudicationTrigger>(c =>
            {
                c.BaseAddress = new Uri(functionsBase);
                c.Timeout = TimeSpan.FromSeconds(10);
            });
        }
        else
        {
            services.AddScoped<IAdjudicationTrigger, InProcessAdjudicationTrigger>();
        }

        services.AddHostedService<OutboxDispatcher>();
        return services;
    }
}
