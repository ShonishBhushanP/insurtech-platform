using InsurTech.Fraud.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace InsurTech.Fraud.Api.Infrastructure;

/// <summary>Tracks recent scores (for duplicate/velocity detection) and open fraud cases.</summary>
public class FraudDbContext(DbContextOptions<FraudDbContext> options) : DbContext(options)
{
    public DbSet<FraudCase> Cases => Set<FraudCase>();
    public DbSet<ScoreRecord> Scores => Set<ScoreRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<FraudCase>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.ClaimId);
            e.HasIndex(c => c.PolicyId);
        });
        b.Entity<ScoreRecord>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.PolicyId);
        });
    }
}

/// <summary>Append-only score log — feeds the duplicate/velocity feature (LLD A.2.5 ShapRecord analogue).</summary>
public class ScoreRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClaimId { get; set; }
    public Guid PolicyId { get; set; }
    public decimal ClaimedAmount { get; set; }
    public double Score { get; set; }
    public string Decision { get; set; } = default!;
    public DateTimeOffset ScoredUtc { get; set; } = DateTimeOffset.UtcNow;
}
