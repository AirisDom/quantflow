using Microsoft.EntityFrameworkCore;

namespace QuantFlow.Orchestrator.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TradeRecord> TradeRecords => Set<TradeRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TradeRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Asset).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Side).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Quantity).HasPrecision(18, 8);
            entity.Property(e => e.Price).HasPrecision(18, 8);
        });
    }
}

public class TradeRecord
{
    public Guid Id { get; set; }
    public string Asset { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime Timestamp { get; set; }
}
