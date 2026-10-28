using System.ComponentModel.DataAnnotations;

namespace PersonalWorkbench.Models;

public sealed class MenuCategory
{
    [Required, MaxLength(64), RegularExpression("^[a-z][a-z0-9-]*$")]
    public string Id { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
}

public sealed class MenuRequest
{
    public const string RoutePattern = "^/tools/[a-z0-9]+(?:-[a-z0-9]+)*$";
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;
    [Required, MaxLength(200), RegularExpression(RoutePattern)]
    public string Route { get; set; } = string.Empty;
    [Required, MaxLength(80), RegularExpression("^[A-Za-z][A-Za-z0-9]*View$")]
    public string ComponentKey { get; set; } = string.Empty;
    [Required, MaxLength(64)]
    public string Category { get; set; } = string.Empty;
    [Required, MaxLength(40), RegularExpression("^[a-z][a-z0-9-]*$")]
    public string Icon { get; set; } = "grid";
    [Required, RegularExpression("^(blue|teal|amber|rose|indigo)$")]
    public string Tone { get; set; } = "blue";
    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;
    [MaxLength(2000)]
    public string Keywords { get; set; } = string.Empty;
    [MaxLength(30)]
    public string[] Features { get; set; } = [];
    public int Order { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed record ToolCatalogResponse(IReadOnlyList<ToolDefinition> Tools, IReadOnlyList<MenuCategory> Categories);
