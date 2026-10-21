using Automation.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Automation.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ReviewJob> ReviewJobs => Set<ReviewJob>();
    public DbSet<ReviewFinding> ReviewFindings => Set<ReviewFinding>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ReviewJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventId).IsUnique();               // idempotency
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.Property(x => x.EventId).HasMaxLength(200);
            e.Property(x => x.TaskId).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.WriteBackStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasMany(x => x.Findings).WithOne().HasForeignKey(f => f.ReviewJobId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.DeliveryAttempts).WithOne().HasForeignKey(a => a.ReviewJobId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReviewFinding>().HasKey(x => x.Id);
        b.Entity<DeliveryAttempt>().HasKey(x => x.Id);
    }
}
