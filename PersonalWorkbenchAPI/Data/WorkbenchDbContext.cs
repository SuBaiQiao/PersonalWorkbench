using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Data;

public sealed class WorkbenchDbContext(DbContextOptions<WorkbenchDbContext> options) : DbContext(options)
{
    public DbSet<DeltaCard> DeltaCards => Set<DeltaCard>();
    public DbSet<ToolDefinition> ToolMenus => Set<ToolDefinition>();
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MenuCategory>(entity =>
        {
            entity.ToTable("menu_categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Id).HasMaxLength(64);
            entity.Property(category => category.Name).HasMaxLength(80).IsRequired();
        });
        modelBuilder.Entity<ToolDefinition>(entity =>
        {
            entity.ToTable("tool_menus");
            entity.HasKey(tool => tool.Id);
            entity.Ignore(tool => tool.DisplayIcon);
            entity.Property(tool => tool.Name).HasMaxLength(120).IsRequired();
            entity.Property(tool => tool.Route).HasMaxLength(200).UseCollation("NOCASE").IsRequired();
            entity.HasIndex(tool => tool.Route).IsUnique();
            entity.Property(tool => tool.ComponentKey).HasMaxLength(80).IsRequired();
            entity.Property(tool => tool.Category).HasMaxLength(64).IsRequired();
            entity.HasOne<MenuCategory>().WithMany().HasForeignKey(tool => tool.Category).OnDelete(DeleteBehavior.Restrict);
            entity.Property(tool => tool.Features).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<string[]>(value, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
                .Metadata.SetValueComparer(new ValueComparer<string[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                    value => value.ToArray()));
        });
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
