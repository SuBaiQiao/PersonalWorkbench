using Microsoft.EntityFrameworkCore;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Data;

public sealed class WorkbenchDbContext(DbContextOptions<WorkbenchDbContext> options) : DbContext(options)
{
    public DbSet<DeltaCard> DeltaCards => Set<DeltaCard>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeltaCard>(entity =>
        {
            entity.ToTable("delta_cards");
            entity.HasKey(card => card.Id);
            entity.Property(card => card.Name).HasMaxLength(120).IsRequired();
            entity.Property(card => card.Category).HasMaxLength(64).IsRequired();
            entity.Property(card => card.Rarity).HasMaxLength(32).IsRequired();
            entity.Property(card => card.Description).HasMaxLength(1000);
            entity.Property(card => card.ImageUrl).HasMaxLength(500);
            entity.Property(card => card.Notes).HasMaxLength(1000);
            entity.Property(card => card.CreatedAtUtc).IsRequired();
            entity.Property(card => card.UpdatedAtUtc).IsRequired();
            entity.HasIndex(card => new { card.Category, card.Rarity });
            entity.HasIndex(card => card.IsCollected);
        });
    }
}
