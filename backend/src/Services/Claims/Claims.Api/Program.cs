using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Persistence;
using InsurTech.Claims.Api.Endpoints;
using InsurTech.Claims.Infrastructure;
using InsurTech.Claims.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);
builder.Services.AddClaimsInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseInsurTechDefaults();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ClaimsDbContext>().EnsureInsurTechSchemaAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "claims" }));
app.MapClaimsEndpoints();

app.Run();
