using InsurTech.Policy.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace InsurTech.Policy.Api.Infrastructure;

public class PolicyDbContext(DbContextOptions<PolicyDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Policy> Policies => Set<Domain.Policy>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Domain.Policy>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.PolicyNumber).IsRequired();
            e.HasIndex(p => p.PolicyNumber).IsUnique();
            e.Ignore(p => p.DomainEvents);
            e.OwnsMany(p => p.Coverages); // owned collection (separate table in SQL MI; in-memory for local)
        });
    }
}
