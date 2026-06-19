using System.Collections.Concurrent;
using InsurTech.BuildingBlocks.Hosting;
using InsurTech.BuildingBlocks.Results;
using InsurTech.BuildingBlocks.Web;

// Notification service (LLD A.8). Email / SMS / WhatsApp delivery + template management.
// Consumes Event Grid domain events in Azure (e.g. ClaimApproved → SendReceipt). Provider is
// abstracted (Dapr binding in prod); here delivery is recorded in-memory.

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInsurTechKeyVault(builder.Configuration);
builder.Services.AddInsurTechDefaults(builder.Configuration);

var app = builder.Build();
app.UseInsurTechDefaults();

var deliveries = new ConcurrentDictionary<string, DeliveryAttempt>();
string[] channels = { "Email", "SMS", "WhatsApp" };
var templates = new[]
{
    new Template("tpl_welcome", "Welcome / policy issued", new[] { "Email", "SMS" }),
    new Template("tpl_claim_received", "Claim received (FNOL ack)", new[] { "Email", "SMS", "WhatsApp" }),
    new Template("tpl_claim_settled", "Claim settled / payout receipt", new[] { "Email", "SMS" }),
    new Template("tpl_claim_referred", "Claim under review", new[] { "Email" })
};

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "notification" }));

// POST /v1/notifications/send
app.MapPost("/v1/notifications/send", (SendRequest req) =>
{
    if (!channels.Contains(req.Channel))
        return Error.Validation("NT-001", $"channel must be one of: {string.Join(", ", channels)}.").ToProblem("notification");
    if (string.IsNullOrWhiteSpace(req.To) || string.IsNullOrWhiteSpace(req.TemplateId))
        return Error.Validation("NT-001", "to and templateId are required.").ToProblem("notification");
    if (!templates.Any(t => t.TemplateId == req.TemplateId))
        return Error.NotFound("NT-010", $"template '{req.TemplateId}' not found.").ToProblem("notification");

    var attempt = new DeliveryAttempt($"ntf_{Guid.NewGuid():N}"[..16], req.Channel, req.To, req.TemplateId, "Queued", DateTimeOffset.UtcNow);
    deliveries[attempt.NotificationId] = attempt;
    return Results.Accepted($"/v1/notifications/{attempt.NotificationId}", attempt);
}).WithTags("Notification");

app.MapGet("/v1/notifications/{id}", (string id) =>
    deliveries.TryGetValue(id, out var d) ? Results.Ok(d)
        : Error.NotFound("NT-010", "Notification not found.").ToProblem("notification")
).WithTags("Notification");

// GET /v1/notifications/templates
app.MapGet("/v1/notifications/templates", () => Results.Ok(templates)).WithTags("Notification");

app.Run();

record SendRequest(string Channel, string To, string TemplateId, Dictionary<string, string>? Data);
record DeliveryAttempt(string NotificationId, string Channel, string To, string TemplateId, string Status, DateTimeOffset QueuedUtc);
record Template(string TemplateId, string Description, string[] Channels);
