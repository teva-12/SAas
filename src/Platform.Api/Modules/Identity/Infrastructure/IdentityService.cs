using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public enum IdentityError
{
    None = 0, InvalidEmail, WeakPassword, EmailTaken, InvalidCredentials, LockedOut, InvalidRefreshToken
}

public sealed record TokenPair(string AccessToken, DateTime AccessTokenExpiresUtc,
    string RefreshToken, DateTime RefreshTokenExpiresUtc);

public sealed record IdentityResult<T>(T? Value, IdentityError Error)
{
    public bool Succeeded => Error == IdentityError.None;
    public static IdentityResult<T> Ok(T value) => new(value, IdentityError.None);
    public static IdentityResult<T> Fail(IdentityError error) => new(default, error);
}

public sealed class IdentityService(
    PlatformDbContext db,
    IPasswordHasher<User> hasher,
    ITokenService tokens,
    IOptions<JwtOptions> jwt,
    TimeProvider clock)
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Verified against when the email is unknown, so "no such user" costs about the same as "wrong password".
    private static readonly User DummyUser = new() { Email = "x@x.invalid", NormalizedEmail = "x@x.invalid", PasswordHash = "" };
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(DummyUser, Guid.NewGuid().ToString("N"));

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IdentityResult<Guid>> RegisterAsync(string? email, string? password, CancellationToken ct)
    {
        if (!TryNormalizeEmail(email, out var normalized)) return IdentityResult<Guid>.Fail(IdentityError.InvalidEmail);
        if (password is null || password.Length < 10 || password.Length > 128)
            return IdentityResult<Guid>.Fail(IdentityError.WeakPassword);

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            return IdentityResult<Guid>.Fail(IdentityError.EmailTaken);

        var user = new User
        {
            UserId = Guid.NewGuid(),
            Email = email!.Trim(),
            NormalizedEmail = normalized,
            PasswordHash = "",
            Status = UserStatus.Active,
            CreatedAtUtc = Now
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
                return IdentityResult<Guid>.Fail(IdentityError.EmailTaken); // lost a race on the unique index
            throw;
        }

        return IdentityResult<Guid>.Ok(user.UserId);
    }

    public async Task<IdentityResult<TokenPair>> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        var fail = IdentityResult<TokenPair>.Fail(IdentityError.InvalidCredentials);
        if (!TryNormalizeEmail(email, out var normalized) || string.IsNullOrEmpty(password)) return fail;

        var now = Now;
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            hasher.VerifyHashedPassword(DummyUser, DummyHash, password);
            return fail;
        }
        if (user.Status != UserStatus.Active) return fail;
        if (user.LockoutEndUtc > now) return IdentityResult<TokenPair>.Fail(IdentityError.LockedOut);

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEndUtc = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);
            return fail;
        }
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;

        var pair = IssueTokens(user, Guid.NewGuid(), now, out _);
        await db.SaveChangesAsync(ct);
        return IdentityResult<TokenPair>.Ok(pair);
    }

    public async Task<IdentityResult<TokenPair>> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        var fail = IdentityResult<TokenPair>.Fail(IdentityError.InvalidRefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken)) return fail;

        var now = Now;
        var hash = tokens.Hash(refreshToken);
        var current = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null) return fail;

        if (current.RevokedAtUtc is not null)
        {
            // A rotated/revoked token came back: assume it was stolen and kill the whole family.
            await RevokeFamilyAsync(current.FamilyId, now, ct);
            return fail;
        }
        if (current.ExpiresAtUtc <= now) return fail;

        var user = await db.Users.SingleOrDefaultAsync(u => u.UserId == current.UserId, ct);
        if (user is null || user.Status != UserStatus.Active) return fail;

        var pair = IssueTokens(user, current.FamilyId, now, out var next);
        current.RevokedAtUtc = now;
        current.ReplacedByTokenId = next.RefreshTokenId;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return fail; // two parallel refreshes with the same token: only one may win
        }
        return IdentityResult<TokenPair>.Ok(pair);
    }

    /// <summary>Idempotent: unknown tokens are ignored.</summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = tokens.Hash(refreshToken);
        var current = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is not null) await RevokeFamilyAsync(current.FamilyId, Now, ct);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct)
    {
        var family = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in family) t.RevokedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    private TokenPair IssueTokens(User user, Guid familyId, DateTime now, out RefreshToken entity)
    {
        var (access, accessExpires) = tokens.CreateAccessToken(user, now);
        var (raw, hash) = tokens.CreateRefreshToken();
        var refreshExpires = now.AddDays(jwt.Value.RefreshTokenDays);
        entity = new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            UserId = user.UserId,
            FamilyId = familyId,
            TokenHash = hash,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpires
        };
        db.RefreshTokens.Add(entity);
        return new TokenPair(access, accessExpires, raw, refreshExpires);
    }

    private static bool TryNormalizeEmail(string? email, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(email)) return false;
        var trimmed = email.Trim();
        if (trimmed.Length > 254) return false;
        if (!MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed) return false;
        normalized = trimmed.ToLowerInvariant();
        return true;
    }
}
