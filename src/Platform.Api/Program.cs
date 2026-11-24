using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platform.BuildingBlocks.TenantContext;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Catalog.Api;
using Platform.Modules.Identity.Api;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Tenancy.Api;
using Platform.Modules.Tenancy.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TenancyOptions>(builder.Configuration.GetSection("Tenancy"));
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());

builder.Services.AddDbContext<PlatformDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Platform")));

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddProblemDetails();

// Auth endpoints: 10 requests/minute per IP (spec 16.1). Behind a reverse proxy, configure
// ForwardedHeaders first so RemoteIpAddress is the real client; also add per-account limits later.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();

// Order matters: tenant + membership must be resolved BEFORE authorization, because permission
// checks depend on the caller's role in the resolved tenant. Path prefixes stand in for separate hosts.
app.UseWhen(c => c.Request.Path.StartsWithSegments("/api/v1/storefront"),
    b => b.UseMiddleware<StorefrontTenantMiddleware>());
app.UseWhen(c => c.Request.Path.StartsWithSegments("/api/v1/merchant"),
    b => b.UseMiddleware<MerchantTenantMiddleware>());

app.UseAuthorization();

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapMerchantEndpoints();
app.MapGet("/api/v1/storefront/whoami", (ITenantContext t) => Results.Ok(new { t.TenantId, t.Source }));

app.Run();

public partial class Program;
