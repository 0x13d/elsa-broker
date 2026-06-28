using MassTransit;
using ElsaBroker.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElsaBroker.Data;

public class BrokerDbContext(DbContextOptions<BrokerDbContext> options) : DbContext(options)
{
    public DbSet<RequestRecord>         RequestRecords         => Set<RequestRecord>();
    public DbSet<RequestTypeDefinition> RequestTypeDefinitions => Set<RequestTypeDefinition>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ── RequestRecord ─────────────────────────────────────────────────────
        b.Entity<RequestRecord>(e =>
        {
            e.HasKey(r => r.CorrelationId);
            e.Property(r => r.Status).HasMaxLength(20);
            e.Property(r => r.ClientId).HasMaxLength(100);
            e.Property(r => r.RequestType).HasMaxLength(100);
            e.HasIndex(r => new { r.ClientId, r.RequestType });
            e.HasIndex(r => r.Status);
        });

        // ── RequestTypeDefinition ─────────────────────────────────────────────
        b.Entity<RequestTypeDefinition>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.ClientId, r.RequestType }).IsUnique();
            e.Property(r => r.ClientId).HasMaxLength(100);
            e.Property(r => r.RequestType).HasMaxLength(100);
        });

        // ── MassTransit saga state ────────────────────────────────────────────
        // The saga map is registered separately in each service's DI setup,
        // but the DbContext must include it for migrations.
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}
