using InsurTech.Policy.Api.Domain;

namespace InsurTech.Policy.Api.Infrastructure;

/// <summary>Seeds demo policies so the Claims flow has something to reference on first run.</summary>
public static class PolicySeeder
{
    public static async Task SeedAsync(PolicyDbContext db)
    {
        if (db.Policies.Any()) return;

        var motor = Domain.Policy.Issue(
            "MOTOR-COMPREHENSIVE-V3", "usr_8b2", "R. Sharma", "kyc_a1f9",
            new[] { new Coverage { Code = "OD", Limit = 1_100_000m }, new Coverage { Code = "TP", Limit = 7_500_000m } },
            12, new DateOnly(2026, 1, 1), 21_771m, 1_100_000m, "INR");

        var health = Domain.Policy.Issue(
            "HEALTH-FAMILY-FLOATER-V2", "usr_8b2", "R. Sharma", "kyc_a1f9",
            new[] { new Coverage { Code = "HOSP", Limit = 1_000_000m } },
            12, new DateOnly(2026, 3, 1), 28_400m, 1_000_000m, "INR");

        db.Policies.AddRange(motor, health);
        await db.SaveChangesAsync();
    }
}
