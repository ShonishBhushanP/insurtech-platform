// API Gateway — local stand-in for Azure API Management Premium (internal mode).
// Presents a single origin (http://localhost:8080) to the MFEs and reverse-proxies to the
// eight domain services, mirroring the edge path AFD → App Gateway WAFv2 → APIM (LLD §4.1).

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins("http://localhost:5173", "http://localhost:4173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors("frontend");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "gateway" }));
app.MapReverseProxy();

app.Run();
