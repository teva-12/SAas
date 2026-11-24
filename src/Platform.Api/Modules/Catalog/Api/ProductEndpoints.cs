using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Platform.BuildingBlocks.TenantContext;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Catalog.Domain;
using Platform.Modules.Identity.Application;
using Platform.Modules.Identity.Infrastructure;

namespace Platform.Modules.Catalog.Api;

// Note there is no TenantId on the request: the tenant always comes from the verified context.
public sealed record CreateProductRequest(string? Name, string? Slug, string? Description);
public sealed record ProductDto(Guid ProductId, string Name, string Slug, string Status);

public static partial class ProductEndpoints
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex ProductSlugRegex();

    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        var merchant = app.MapGroup("/api/v1/merchant").RequireAuthorization();

        merchant.MapGet("/me", (ITenantContext tenant, IMembershipContext membership) =>
            Results.Ok(new
            {
                tenant.TenantId,
                Role = membership.RoleCode,
                Permissions = Roles.PermissionsFor(membership.RoleCode ?? "").Order().ToList()
            }));

        merchant.MapGet("/products", async (PlatformDbContext db, CancellationToken ct) =>
        {
            // TODO: cursor pagination (spec 14.1). Capped for now.
            var rows = await db.Products.AsNoTracking()
                .OrderByDescending(p => p.CreatedAtUtc)
                .Select(p => new { p.ProductId, p.Name, p.Slug, p.Status })
                .Take(100)
                .ToListAsync(ct);
            return Results.Ok(rows.Select(r => new ProductDto(r.ProductId, r.Name, r.Slug, r.Status.ToString())));
        }).RequirePermission(Permissions.CatalogRead);

        merchant.MapPost("/products", async (CreateProductRequest req, PlatformDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 200)
                errors["name"] = ["Name is required (max 200 characters)."];
            var slug = (req.Slug ?? "").Trim().ToLowerInvariant();
            if (slug.Length is < 1 or > 220 || !ProductSlugRegex().IsMatch(slug))
                errors["slug"] = ["Slug must be lowercase letters, digits and single hyphens (max 220)."];
            if (req.Description is { Length: > 20000 })
                errors["description"] = ["Description is too long."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var now = clock.GetUtcNow().UtcDateTime;
            var product = new Product
            {
                ProductId = Guid.NewGuid(),
                Name = req.Name!.Trim(),
                Slug = slug,
                Description = req.Description,
                Status = ProductStatus.Draft,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Products.Add(product); // TenantId is stamped by PlatformDbContext from the trusted context

            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                return Results.Problem(statusCode: 409, title: "A product with this slug already exists.");
            }
            return Results.Created($"/api/v1/merchant/products/{product.ProductId}",
                new ProductDto(product.ProductId, product.Name, product.Slug, product.Status.ToString()));
        }).RequirePermission(Permissions.CatalogWrite);

        return app;
    }
}
