using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platform.BuildingBlocks.Authorization;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Tenancy.Domain;
using System.Security.Claims;

namespace Platform.Modules.Identity.Api;

public sealed record CredentialsRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (CredentialsRequest req, IdentityService svc, CancellationToken ct) =>
        {
            var result = await svc.RegisterAsync(req.Email, req.Password, ct);
            return result.Error switch
            {
                IdentityError.None => Results.Created("/api/v1/me/tenants", new { userId = result.Value }),
                IdentityError.InvalidEmail => Results.Problem(statusCode: 400, title: "Invalid email address."),
                IdentityError.WeakPassword => Results.Problem(statusCode: 400, title: "Password must be 10 to 128 characters."),
                IdentityError.EmailTaken => Results.Problem(statusCode: 409, title: "Email is already registered."),
                _ => Results.Problem(statusCode: 500)
            };
        });

        auth.MapPost("/login", async (CredentialsRequest req, IdentityService svc, CancellationToken ct) =>
        {
            var result = await svc.LoginAsync(req.Email, req.Password, ct);
            return result.Error switch
            {
                IdentityError.None => Results.Ok(result.Value),
                IdentityError.LockedOut => Results.Problem(statusCode: 429, title: "Too many failed attempts. Try again later."),
                _ => Results.Problem(statusCode: 401, title: "Invalid email or password.") // never say which part was wrong
            };
        });

        auth.MapPost("/refresh", async (RefreshRequest req, IdentityService svc, CancellationToken ct) =>
        {
            var result = await svc.RefreshAsync(req.RefreshToken, ct);
            return result.Succeeded
                ? Results.Ok(result.Value)
                : Results.Problem(statusCode: 401, title: "Invalid refresh token.");
        });

        auth.MapPost("/logout", async (RefreshRequest req, IdentityService svc, CancellationToken ct) =>
        {
            await svc.LogoutAsync(req.RefreshToken, ct);
            return Results.NoContent();
        });

        // Which tenants can this user act in? The client then sends X-Tenant-Id for merchant calls.
        app.MapGet("/api/v1/me/tenants", async (ClaimsPrincipal user, PlatformDbContext db, CancellationToken ct) =>
        {
            if (CurrentUser.GetUserId(user) is not { } userId) return Results.Unauthorized();
            var items = await db.TenantMemberships.AsNoTracking()
                .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && m.Tenant.Status == TenantStatus.Active)
                .Select(m => new { m.TenantId, m.Tenant.Slug, m.Tenant.Name, Role = m.RoleCode })
                .ToListAsync(ct);
            return Results.Ok(items);
        }).RequireAuthorization();

        return app;
    }
}
