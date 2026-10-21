using System.ComponentModel.DataAnnotations;

namespace PersonalWorkbench.Models;

public sealed class DeltaCard
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Category { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Rarity { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500), Url]
    public string? ImageUrl { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public bool IsCollected { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class CreateDeltaCardRequest
{
    [Required, MaxLength(120)]
    public string? Name { get; set; }

    [Required, MaxLength(64)]
    public string? Category { get; set; }

    [Required, MaxLength(32)]
    public string? Rarity { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500), Url]
    public string? ImageUrl { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public bool IsCollected { get; set; }
}

public sealed class UpdateDeltaCardRequest
{
    [Required, MaxLength(120)]
    public string? Name { get; set; }

    [Required, MaxLength(64)]
    public string? Category { get; set; }

    [Required, MaxLength(32)]
    public string? Rarity { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500), Url]
    public string? ImageUrl { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public bool IsCollected { get; set; }
}

public sealed record DeltaCardResponse(
    int Id,
    string Name,
    string Category,
    string Rarity,
    string? Description,
    string? ImageUrl,
    string? Notes,
    bool IsCollected,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
