// API Gateway — local stand-in for Azure API Management Premium (internal mode).
// Presents a single origin (http://localhost:8080) to the MFEs and reverse-proxies to the
// eight domain services, mirroring the edge path AFD → App Gateway WAFv2 → APIM (LLD §4.1).

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Allowed browser origins: local dev + the deployed Static Web App. Extra origins can be
// appended via config key "Cors:Origins" (comma-separated) without a rebuild.
var corsOrigins = new List<string>
{
    "http://localhost:5173", "http://localhost:4173",
    "https://red-water-0a9d64f00.7.azurestaticapps.net"
};
var extra = builder.Configuration["Cors:Origins"];
if (!string.IsNullOrWhiteSpace(extra))
    corsOrigins.AddRange(extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(corsOrigins.ToArray())
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// When served same-origin behind an Azure Static Web Apps linked backend, the SWA proxies the
// app's "/api/*" calls here. Strip a leading "/api" segment so the existing "/v1/..." routes
// match unchanged (no-op for direct "/v1/..." calls in local/dev). This MUST run before routing,
// so UseRouting() is called explicitly below — otherwise the framework inserts routing at the
// start of the pipeline and matches the un-stripped "/api/v1/..." path (→ 404).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api", out var rest))
        ctx.Request.Path = rest.HasValue ? rest : "/";
    await next();
});

app.UseRouting();
app.UseCors("frontend");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "gateway" }));
app.MapReverseProxy();

app.Run();
