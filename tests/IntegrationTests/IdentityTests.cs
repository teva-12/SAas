using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.BuildingBlocks.TenantContext;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Application;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;

namespace IntegrationTests;

public class IdentityTests
{
    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private const string Email = "owner@example.com";
    private const string Password = "correct-horse-battery";

    private static (IdentityService Svc, TestClock Clock, PlatformDbContext Db) Build()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new PlatformDbContext(options, new TenantContext());
        var jwt = Options.Create(new JwtOptions { SigningKey = new string('k', 64) });
        var svc = new IdentityService(db, new PasswordHasher<User>(), new TokenService(jwt), jwt, clock);
        return (svc, clock, db);
    }

    private static async Task<IdentityService> Registered(IdentityService svc)
    {
        var r = await svc.RegisterAsync(Email, Password, CancellationToken.None);
        Assert.True(r.Succeeded);
        return svc;
    }

    [Fact]
    public async Task Register_then_login_returns_both_tokens()
    {
        var (svc, _, _) = Build();
        await Registered(svc);
        var login = await svc.LoginAsync(Email, Password, CancellationToken.None);
        Assert.True(login.Succeeded);
        Assert.False(string.IsNullOrEmpty(login.Value!.AccessToken));
        Assert.False(string.IsNullOrEmpty(login.Value.RefreshToken));
    }

    [Fact]
    public async Task Short_password_and_bad_email_are_rejected()
    {
        var (svc, _, _) = Build();
        Assert.Equal(IdentityError.WeakPassword, (await svc.RegisterAsync(Email, "short", default)).Error);
        Assert.Equal(IdentityError.InvalidEmail, (await svc.RegisterAsync("not-an-email", Password, default)).Error);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        var (svc, _, _) = Build();
        await Registered(svc);
        var again = await svc.RegisterAsync("OWNER@Example.com", Password, default);
        Assert.Equal(IdentityError.EmailTaken, again.Error);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_look_identical()
    {
        var (svc, _, _) = Build();
        await Registered(svc);
        Assert.Equal(IdentityError.InvalidCredentials, (await svc.LoginAsync(Email, "wrong-password-1", default)).Error);
        Assert.Equal(IdentityError.InvalidCredentials, (await svc.LoginAsync("nobody@example.com", Password, default)).Error);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_until_the_lockout_expires()
    {
        var (svc, clock, _) = Build();
        await Registered(svc);

        for (var i = 0; i < IdentityService.MaxFailedLogins; i++)
            Assert.Equal(IdentityError.InvalidCredentials, (await svc.LoginAsync(Email, "wrong-password-1", default)).Error);

        // Even the correct password is refused while locked out.
        Assert.Equal(IdentityError.LockedOut, (await svc.LoginAsync(Email, Password, default)).Error);

        clock.Now = clock.Now.Add(IdentityService.LockoutDuration + TimeSpan.FromMinutes(1));
        Assert.True((await svc.LoginAsync(Email, Password, default)).Succeeded);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_revokes_the_whole_family()
    {
        var (svc, _, _) = Build();
        await Registered(svc);
        var first = (await svc.LoginAsync(Email, Password, default)).Value!;

        var second = await svc.RefreshAsync(first.RefreshToken, default);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.RefreshToken, second.Value!.RefreshToken);

        // Replaying the already-rotated token is treated as theft...
        Assert.Equal(IdentityError.InvalidRefreshToken, (await svc.RefreshAsync(first.RefreshToken, default)).Error);
        // ...and kills the newer token from the same family too.
        Assert.Equal(IdentityError.InvalidRefreshToken, (await svc.RefreshAsync(second.Value.RefreshToken, default)).Error);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        var (svc, clock, _) = Build();
        await Registered(svc);
        var tokens = (await svc.LoginAsync(Email, Password, default)).Value!;
        clock.Now = clock.Now.AddDays(31);
        Assert.Equal(IdentityError.InvalidRefreshToken, (await svc.RefreshAsync(tokens.RefreshToken, default)).Error);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var (svc, _, _) = Build();
        await Registered(svc);
        var tokens = (await svc.LoginAsync(Email, Password, default)).Value!;
        await svc.LogoutAsync(tokens.RefreshToken, default);
        Assert.Equal(IdentityError.InvalidRefreshToken, (await svc.RefreshAsync(tokens.RefreshToken, default)).Error);
    }

    [Fact]
    public async Task Only_a_hash_of_the_refresh_token_is_stored()
    {
        var (svc, _, db) = Build();
        await Registered(svc);
        var tokens = (await svc.LoginAsync(Email, Password, default)).Value!;
        var stored = await db.RefreshTokens.SingleAsync();
        Assert.NotEqual(tokens.RefreshToken, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
    }

    [Fact]
    public void Role_permissions_follow_least_privilege()
    {
        Assert.True(Permissions.All.All(Roles.PermissionsFor(Roles.Owner).Contains));
        Assert.DoesNotContain(Permissions.OrdersRefund, Roles.PermissionsFor(Roles.SupportAgent));
        Assert.DoesNotContain(Permissions.CatalogWrite, Roles.PermissionsFor(Roles.Marketing));
        Assert.DoesNotContain(Permissions.BillingManage, Roles.PermissionsFor(Roles.StoreManager));
        Assert.Empty(Roles.PermissionsFor("made-up-role"));
    }
}
