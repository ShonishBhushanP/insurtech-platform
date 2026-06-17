using Azure.Monitor.OpenTelemetry.AspNetCore;
using InsurTech.BuildingBlocks.Azure;
using InsurTech.BuildingBlocks.Idempotency;
using InsurTech.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace InsurTech.BuildingBlocks.Hosting;

/// <summary>
/// Cross-cutting wiring shared by every service host. Each Azure dependency is activated only
/// when its configuration is present; otherwise the local in-memory equivalent is used, so the
/// platform runs identically offline and on Azure (deployment diagram: Shared Platform tier).
/// </summary>
public static class ServiceDefaults
{
    public const string DevCorsPolicy = "insurtech-dev-cors";

    /// <summary>
    /// Adds the Azure Key Vault configuration source when <c>Azure:KeyVault:Uri</c> is set, so
    /// secrets (connection strings, keys) resolve from Key Vault via Managed Identity (LLD IR-04).
    /// Call early in Program.cs on the builder's configuration.
    /// </summary>
    public static IConfigurationBuilder AddInsurTechKeyVault(this IConfigurationBuilder config, IConfiguration bootstrap)
    {
        var kvUri = bootstrap["Azure:KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(kvUri))
            config.AddAzureKeyVault(new Uri(kvUri), AzureCredential.Instance);
        return config;
    }

    public static IServiceCollection AddInsurTechDefaults(this IServiceCollection services, IConfiguration config)
    {
        AddIdempotency(services, config);
        AddEventBus(services, config);
        AddTelemetry(services, config);

        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddCors(o => o.AddPolicy(DevCorsPolicy, p => p
            .WithOrigins(
                "http://localhost:5173", "http://localhost:4173", // Vite dev / preview
                "http://localhost:8080")                           // gateway
            .AllowAnyHeader()
            .AllowAnyMethod()));

        return services;
    }

    private static void AddIdempotency(IServiceCollection services, IConfiguration config)
    {
        var redis = config["Azure:Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
            services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        }
        else
        {
            services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        }
    }

    private static void AddEventBus(IServiceCollection services, IConfiguration config)
    {
        var ns = config["Azure:ServiceBus:FullyQualifiedNamespace"];
        if (!string.IsNullOrWhiteSpace(ns))
        {
            services.AddSingleton(_ => new global::Azure.Messaging.ServiceBus.ServiceBusClient(ns, AzureCredential.Instance));
            services.AddSingleton<IEventBus, ServiceBusEventBus>();
        }
        else
        {
            services.AddSingleton<IEventBus, InMemoryEventBus>();
        }
    }

    private static void AddTelemetry(IServiceCollection services, IConfiguration config)
    {
        // Azure Monitor / Application Insights (deployment diagram: Shared Platform — Monitor).
        var appInsights = config["APPLICATIONINSIGHTS_CONNECTION_STRING"]
                          ?? config["Azure:Monitor:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(appInsights))
            services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = appInsights);
    }

    public static WebApplication UseInsurTechDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseCors(DevCorsPolicy);
        return app;
    }
}
