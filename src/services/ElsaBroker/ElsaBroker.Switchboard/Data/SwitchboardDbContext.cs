using Microsoft.EntityFrameworkCore;

namespace ElsaBroker.Switchboard.Data;

/// <summary>
/// EF Core context for the Switchboard SQLite database.
/// Stores broker log events for the dashboard to query.
/// </summary>
public class SwitchboardDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="SwitchboardDbContext"/>.
    /// </summary>
    public SwitchboardDbContext(DbContextOptions<SwitchboardDbContext> options)
        : base(options)
    {
    }

    /// <summary>All stored log events.</summary>
    public DbSet<LogEventRecord> LogEvents => Set<LogEventRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogEventRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Level).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CorrelationId).HasMaxLength(200);
            entity.Property(e => e.ClientId).HasMaxLength(200);
            entity.Property(e => e.RequestType).HasMaxLength(200);

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.Level);
        });
    }
}
