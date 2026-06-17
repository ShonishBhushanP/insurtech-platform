using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace InsurTech.BuildingBlocks.Persistence;

/// <summary>
/// Selects the EF Core store per the deployment diagram's Data Tier: <b>Azure SQL / SQL MI</b>
/// when a connection string is configured, otherwise the <b>InMemory</b> provider for local runs.
/// </summary>
public static class StoreSelector
{
    /// <param name="connectionName">Key under ConnectionStrings:* (e.g. "ClaimsDb").</param>
    /// <param name="inMemoryName">Local InMemory database name fallback.</param>
    public static DbContextOptionsBuilder UseInsurTechStore(
        this DbContextOptionsBuilder options, IConfiguration config, string connectionName, string inMemoryName)
    {
        var connectionString = config.GetConnectionString(connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
        }
        else
        {
            options.UseInMemoryDatabase(inMemoryName);
        }
        return options;
    }

    /// <summary>True when a real SQL connection string is configured for this context.</summary>
    public static bool IsRelational(IConfiguration config, string connectionName) =>
        !string.IsNullOrWhiteSpace(config.GetConnectionString(connectionName));

    /// <summary>
    /// Ensures this context's schema exists. For InMemory, EnsureCreated. For SQL: if the
    /// database is absent it creates database + schema; if the database already exists (e.g. a
    /// shared, pre-provisioned Azure SQL DB) it creates just this context's tables. Idempotent —
    /// a second run where tables already exist is a no-op.
    /// </summary>
    public static async Task EnsureInsurTechSchemaAsync(this DbContext db, CancellationToken ct = default)
    {
        if (db.Database.IsInMemory())
        {
            await db.Database.EnsureCreatedAsync(ct);
            return;
        }

        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(ct))
        {
            await db.Database.EnsureCreatedAsync(ct); // creates DB + full schema
            return;
        }

        try
        {
            await creator.CreateTablesAsync(ct); // DB exists — add this context's tables
        }
        catch (Exception)
        {
            // Tables already created on a previous run — safe to ignore.
        }
    }
}
