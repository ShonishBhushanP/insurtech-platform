using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;

// Audit & Compliance service (LLD A.9). Immutable, append-only audit log + regulatory report
// generation. In Azure this is ADX-native with a Confidential Ledger hash receipt; here we keep
// an in-memory append-only log with a SHA-256 hash chain (each entry hashes the previous) so the
// log is tamper-evident — altering any record breaks the chain.

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);

var app = builder.Build();
app.UseInsurTechDefaults();

var log = new ConcurrentQueue<AuditEvent>();
var lastHash = "GENESIS";
var gate = new object();

string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "audit" }));

// POST /v1/audit/events — append an immutable audit record (hash-chained)
app.MapPost("/v1/audit/events", (AuditEventRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Action) || string.IsNullOrWhiteSpace(req.Entity))
        return Error.Validation("AUD-001", "action and entity are required.").ToProblem("audit");

    AuditEvent ev;
    lock (gate)
    {
        var ts = DateTimeOffset.UtcNow;
        var id = $"aud_{Guid.NewGuid():N}"[..16];
        var body = JsonSerializer.Serialize(new { req.Actor, req.Action, req.Entity, req.EntityId, req.Payload, ts });
        var hash = Sha256(lastHash + body);
        ev = new AuditEvent(id, req.Actor ?? "system", req.Action, req.Entity, req.EntityId, req.Payload, ts, lastHash, hash);
        lastHash = hash;
        log.Enqueue(ev);
    }
    return Results.Accepted($"/v1/audit/events/{ev.Id}", ev);
}).WithTags("Audit");

// GET /v1/audit/query — filter by entity / entityId / action
app.MapGet("/v1/audit/query", (string? entity, string? entityId, string? action) =>
{
    var items = log.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(entity)) items = items.Where(e => e.Entity.Equals(entity, StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(entityId)) items = items.Where(e => e.EntityId == entityId);
    if (!string.IsNullOrWhiteSpace(action)) items = items.Where(e => e.Action.Equals(action, StringComparison.OrdinalIgnoreCase));
    return Results.Ok(items.OrderByDescending(e => e.Timestamp).Take(500));
}).WithTags("Audit");

// GET /v1/audit/verify — re-walk the hash chain to prove integrity
app.MapGet("/v1/audit/verify", () =>
{
    var prev = "GENESIS"; var intact = true; var count = 0;
    foreach (var e in log)
    {
        var body = JsonSerializer.Serialize(new { e.Actor, e.Action, e.Entity, e.EntityId, e.Payload, ts = e.Timestamp });
        if (e.PrevHash != prev || e.Hash != Sha256(prev + body)) { intact = false; break; }
        prev = e.Hash; count++;
    }
    return Results.Ok(new { entries = log.Count, verified = count, chainIntact = intact });
}).WithTags("Audit");

// GET /v1/reports/{type} — simple regulatory report (e.g. IRDAI claims summary)
app.MapGet("/v1/reports/{type}", (string type) =>
{
    var byAction = log.GroupBy(e => e.Action).ToDictionary(g => g.Key, g => g.Count());
    return Results.Ok(new
    {
        reportId = $"rpt_{Guid.NewGuid():N}"[..16],
        reportType = type,
        generatedUtc = DateTimeOffset.UtcNow,
        totalEvents = log.Count,
        breakdownByAction = byAction,
        note = "Generated from the immutable audit log (ADX + Confidential Ledger in production)."
    });
}).WithTags("Audit");

app.Run();

record AuditEventRequest(string? Actor, string Action, string Entity, string? EntityId, JsonElement? Payload);
record AuditEvent(string Id, string Actor, string Action, string Entity, string? EntityId, JsonElement? Payload,
    DateTimeOffset Timestamp, string PrevHash, string Hash);
