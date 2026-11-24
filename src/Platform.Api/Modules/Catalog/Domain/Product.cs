using Platform.BuildingBlocks.TenantContext;

namespace Platform.Modules.Catalog.Domain;

public enum ProductStatus : byte { Draft = 0, Active = 1, Archived = 2 }

/// <summary>First tenant-owned entity (spec 13.5). Proves the isolation plumbing end to end.</summary>
public sealed class Product : ITenantOwned
{
    public Guid ProductId { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public ProductStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
