using Microsoft.EntityFrameworkCore;
using PersonalWorkbench.Data;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class DeltaCardService(WorkbenchDbContext db)
{
    public async Task<IReadOnlyList<DeltaCardResponse>> GetCardsAsync(
        string? search,
        string? category,
        string? rarity,
        bool? collected,
        CancellationToken cancellationToken)
    {
        var query = db.DeltaCards.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(card => card.Name.Contains(keyword) || (card.Description != null && card.Description.Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(card => card.Category == category.Trim());
        }

        if (!string.IsNullOrWhiteSpace(rarity))
        {
            query = query.Where(card => card.Rarity == rarity.Trim());
        }

        if (collected.HasValue)
        {
            query = query.Where(card => card.IsCollected == collected.Value);
        }

        var result = await query
            .OrderBy(card => card.Category)
            .ThenBy(card => card.Rarity)
            .ThenBy(card => card.Name)
            .ToListAsync(cancellationToken);

        return result.Select(ToResponse).ToArray();
    }

    public async Task<DeltaCardResponse?> GetCardAsync(int id, CancellationToken cancellationToken)
    {
        var card = await db.DeltaCards.AsNoTracking().SingleOrDefaultAsync(card => card.Id == id, cancellationToken);
        return card is null ? null : ToResponse(card);
    }

    public async Task<DeltaCardResponse> CreateCardAsync(CreateDeltaCardRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var card = new DeltaCard
        {
            Name = request.Name!.Trim(),
            Category = request.Category!.Trim(),
            Rarity = request.Rarity!.Trim(),
            Description = NormalizeOptional(request.Description),
            ImageUrl = NormalizeOptional(request.ImageUrl),
            Notes = NormalizeOptional(request.Notes),
            IsCollected = request.IsCollected,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.DeltaCards.Add(card);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(card);
    }

    public async Task<DeltaCardResponse?> UpdateCardAsync(int id, UpdateDeltaCardRequest request, CancellationToken cancellationToken)
    {
        var card = await db.DeltaCards.SingleOrDefaultAsync(card => card.Id == id, cancellationToken);
        if (card is null)
        {
            return null;
        }

        card.Name = request.Name!.Trim();
        card.Category = request.Category!.Trim();
        card.Rarity = request.Rarity!.Trim();
        card.Description = NormalizeOptional(request.Description);
        card.ImageUrl = NormalizeOptional(request.ImageUrl);
        card.Notes = NormalizeOptional(request.Notes);
        card.IsCollected = request.IsCollected;
        card.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(card);
    }

    public async Task<bool> SetCollectedAsync(int id, bool collected, CancellationToken cancellationToken)
    {
        var card = await db.DeltaCards.SingleOrDefaultAsync(card => card.Id == id, cancellationToken);
        if (card is null)
        {
            return false;
        }

        card.IsCollected = collected;
        card.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteCardAsync(int id, CancellationToken cancellationToken)
    {
        var card = await db.DeltaCards.SingleOrDefaultAsync(card => card.Id == id, cancellationToken);
        if (card is null)
        {
            return false;
        }

        db.DeltaCards.Remove(card);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DeltaCardResponse ToResponse(DeltaCard card) => new(
        card.Id,
        card.Name,
        card.Category,
        card.Rarity,
        card.Description,
        card.ImageUrl,
        card.Notes,
        card.IsCollected,
        card.CreatedAtUtc,
        card.UpdatedAtUtc);
}
