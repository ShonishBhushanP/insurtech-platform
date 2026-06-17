using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Persistence;
using InsurTech.Policy.Api.Endpoints;
using InsurTech.Policy.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);
builder.Services.AddDbContext<PolicyDbContext>(o => o.UseInsurTechStore(builder.Configuration, "PolicyDb", "policy-db"));

var app = builder.Build();
app.UseInsurTechDefaults();

// Ensure schema (SQL) / store (InMemory) exists, then seed demo data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PolicyDbContext>();
    await db.EnsureInsurTechSchemaAsync();
    await PolicySeeder.SeedAsync(db);
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "policy" }));
app.MapPolicyEndpoints();

app.Run();
