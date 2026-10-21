using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/delta-cards")]
public sealed class DeltaCardsController(DeltaCardService cards) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeltaCardResponse>>> GetCards(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? rarity,
        [FromQuery] bool? collected,
        CancellationToken cancellationToken)
        => Ok(await cards.GetCardsAsync(search, category, rarity, collected, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DeltaCardResponse>> GetCard(int id, CancellationToken cancellationToken)
    {
        var card = await cards.GetCardAsync(id, cancellationToken);
        return card is null ? NotFound() : Ok(card);
    }

    [HttpPost]
    public async Task<ActionResult<DeltaCardResponse>> CreateCard(CreateDeltaCardRequest request, CancellationToken cancellationToken)
    {
        var card = await cards.CreateCardAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetCard), new { id = card.Id }, card);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DeltaCardResponse>> UpdateCard(int id, UpdateDeltaCardRequest request, CancellationToken cancellationToken)
    {
        var card = await cards.UpdateCardAsync(id, request, cancellationToken);
        return card is null ? NotFound() : Ok(card);
    }

    [HttpPatch("{id:int}/collection")]
    public async Task<IActionResult> SetCollected(int id, [FromBody] SetCollectedRequest request, CancellationToken cancellationToken)
        => await cards.SetCollectedAsync(id, request.IsCollected, cancellationToken) ? NoContent() : NotFound();

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCard(int id, CancellationToken cancellationToken)
        => await cards.DeleteCardAsync(id, cancellationToken) ? NoContent() : NotFound();
}

public sealed record SetCollectedRequest(bool IsCollected);
