using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Persistence;
using InsurTech.Fraud.Api.Endpoints;
using InsurTech.Fraud.Api.Infrastructure;
using InsurTech.Fraud.Api.Scoring;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);
builder.Services.AddDbContext<FraudDbContext>(o => o.UseInsurTechStore(builder.Configuration, "FraudDb", "fraud-db"));
builder.Services.AddSingleton<RiskScorer>();
builder.Services.Configure<FraudOptions>(builder.Configuration.GetSection("Fraud"));

// Optional Azure ML scoring endpoint (LLD A.2 / TR-05). Falls back to the local heuristic.
builder.Services.AddScoringEngine(builder.Configuration);

var app = builder.Build();
app.UseInsurTechDefaults();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<FraudDbContext>().EnsureInsurTechSchemaAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "fraud" }));
app.MapFraudEndpoints();

app.Run();
