using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.BuildingBlocks.Authorization;
using Platform.BuildingBlocks.TenantContext;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Infrastructure;

public sealed class TenancyOptions
{
    /// <summary>Platform root domain; stores live at {slug}.{RootDomain}. Dev: "localtest.me".</summary>
    public string RootDomain { get; set; } = "localtest.me";
}

/// <summary>
/// Storefront: tenant comes ONLY from the Host header, matched against verified
/// domains / active slugs. Unknown hosts get a bare 404 so tenant existence isn't leaked.
/// </summary>
public sealed class StorefrontTenantMiddleware(RequestDelegate next, IOptions<TenancyOptions> options)
{
    public async Task InvokeAsync(HttpContext ctx, PlatformDbContext db, ITenantContextSetter setter)
    {
        var host = ctx.Request.Host.Host.Trim().TrimEnd('.').ToLowerInvariant();
        var root = options.Value.RootDomain.ToLowerInvariant();
        var ct = ctx.RequestAborted;

        // 1) Custom domain: must be fully active (verified + certificate provisioned).
        var tenantId = await db.TenantDomains.AsNoTracking()
            .Where(d => d.Domain == host
                        && d.Status == TenantDomainStatus.Active
                        && d.Tenant.Status == TenantStatus.Active)
            .Select(d => (Guid?)d.TenantId)
            .FirstOrDefaultAsync(ct);
        var source = TenantResolutionSource.CustomDomain;

        // 2) Platform subdomain: exactly one label before the root domain.
        if (tenantId is null && host.EndsWith("." + root, StringComparison.Ordinal))
        {
            var slug = host[..^(root.Length + 1)];
            if (slug.Length > 0 && !slug.Contains('.'))
            {
                tenantId = await db.Tenants.AsNoTracking()
                    .Where(t => t.Slug == slug && t.Status == TenantStatus.Active)
                    .Select(t => (Guid?)t.TenantId)
                    .FirstOrDefaultAsync(ct);
                source = TenantResolutionSource.PlatformSubdomain;
            }
        }

        if (tenantId is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        setter.Set(tenantId.Value, source);
        await next(ctx);
    }
}

/// <summary>
/// Merchant admin: the client SELECTS a tenant via header, but that is never proof of access.
/// We verify an active membership for the authenticated user in an active tenant (spec 12.2),
/// then publish the caller's role so permission checks run against it.
/// </summary>
public sealed class MerchantTenantMiddleware(RequestDelegate next)
{
    public const string TenantHeader = "X-Tenant-Id";

    public async Task InvokeAsync(HttpContext ctx, PlatformDbContext db,
        ITenantContextSetter tenantSetter, IMembershipContextSetter membershipSetter)
    {
        if (CurrentUser.GetUserId(ctx.User) is not { } userId)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!ctx.Request.Headers.TryGetValue(TenantHeader, out var raw) || !Guid.TryParse(raw.ToString(), out var tenantId))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var role = await db.TenantMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.TenantId == tenantId
                        && m.Status == MembershipStatus.Active
                        && m.Tenant.Status == TenantStatus.Active)
            .Select(m => m.RoleCode)
            .FirstOrDefaultAsync(ctx.RequestAborted);

        if (role is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        tenantSetter.Set(tenantId, TenantResolutionSource.MerchantMembership);
        membershipSetter.Set(role);
        await next(ctx);
    }
}
