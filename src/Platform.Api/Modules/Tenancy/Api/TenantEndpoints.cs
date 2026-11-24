using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Platform.BuildingBlocks.Authorization;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Application;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Api;

public sealed record CreateTenantRequest(string? Name, string? Slug, string? Locale, string? TimeZoneId, string? CurrencyCode);

public static partial class TenantRules
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "www", "api", "admin", "app", "mail", "support", "status", "cdn", "static", "assets",
        "billing", "platform", "dashboard", "merchant", "login", "auth", "help", "docs"
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyRegex();

    public static Dictionary<string, string[]> Validate(CreateTenantRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length is < 2 or > 200)
            errors["name"] = ["Name must be 2 to 200 characters."];

        var slug = (r.Slug ?? "").Trim().ToLowerInvariant();
        if (!SlugRegex().IsMatch(slug))
            errors["slug"] = ["Slug must be 3 to 63 characters: lowercase letters, digits and hyphens, not starting or ending with a hyphen."];
        else if (Reserved.Contains(slug))
            errors["slug"] = ["This slug is reserved."];

        if (!CurrencyRegex().IsMatch((r.CurrencyCode ?? "").Trim().ToUpperInvariant()))
            errors["currencyCode"] = ["Use a 3-letter ISO 4217 code. TBD: restrict to the supported currency list."];

        try { _ = CultureInfo.GetCultureInfo((r.Locale ?? "").Trim()); }
        catch (CultureNotFoundException) { errors["locale"] = ["Unknown locale, e.g. en or ar-EG."]; }

        try { _ = TimeZoneInfo.FindSystemTimeZoneById((r.TimeZoneId ?? "").Trim()); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { errors["timeZoneId"] = ["Unknown time zone id."]; }

        return errors;
    }
}

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        // Self-serve onboarding: any signed-in user can open a store and becomes its owner.
        // TODO: per-user tenant creation limit, email verification gate, abuse monitoring.
        app.MapPost("/api/v1/tenants", async (CreateTenantRequest req, ClaimsPrincipal user,
            PlatformDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (CurrentUser.GetUserId(user) is not { } userId) return Results.Unauthorized();

            var errors = TenantRules.Validate(req);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var slug = req.Slug!.Trim().ToLowerInvariant();
            if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
                return Results.Problem(statusCode: 409, title: "Slug is already in use.");

            var now = clock.GetUtcNow().UtcDateTime;
            var tenant = new Tenant
            {
                TenantId = Guid.NewGuid(),
                Slug = slug,
                Name = req.Name!.Trim(),
                Status = TenantStatus.Active, // TODO: Pending until onboarding/billing checks exist
                Locale = req.Locale!.Trim(),
                TimeZoneId = req.TimeZoneId!.Trim(),
                CurrencyCode = req.CurrencyCode!.Trim().ToUpperInvariant(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Tenants.Add(tenant);
            db.TenantMemberships.Add(new TenantMembership
            {
                TenantMembershipId = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = userId,
                RoleCode = Roles.Owner,
                Status = MembershipStatus.Active,
                CreatedAtUtc = now
            });

            try { await db.SaveChangesAsync(ct); } // tenant + owner membership commit atomically
            catch (DbUpdateException)
            {
                return Results.Problem(statusCode: 409, title: "Could not create the store. The slug may have just been taken.");
            }

            return Results.Created($"/api/v1/me/tenants", new { tenant.TenantId, tenant.Slug, Role = Roles.Owner });
        }).RequireAuthorization();

        return app;
    }
}
