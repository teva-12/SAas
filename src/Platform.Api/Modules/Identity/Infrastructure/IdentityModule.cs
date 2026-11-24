using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Platform.BuildingBlocks.TenantContext;
using Platform.Modules.Identity.Application;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Grants a permission if the caller's role IN THE RESOLVED TENANT includes it.
/// Runs after the merchant tenant middleware has verified membership.</summary>
public sealed class PermissionHandler(IMembershipContext membership) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (membership.RoleCode is { } role && Roles.PermissionsFor(role).Contains(requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (Encoding.UTF8.GetByteCount(jwt.SigningKey ?? "") < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes. Set it with user-secrets or the Jwt__SigningKey environment variable.");

        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IdentityService>();

        services.AddScoped<MembershipContext>();
        services.AddScoped<IMembershipContext>(sp => sp.GetRequiredService<MembershipContext>());
        services.AddScoped<IMembershipContextSetter>(sp => sp.GetRequiredService<MembershipContext>());

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; // keep "sub" as "sub"
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey!)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

        services.AddAuthorization(o =>
        {
            foreach (var permission in Permissions.All)
                o.AddPolicy(permission, p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
        });
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        return services;
    }

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(permission);
}
