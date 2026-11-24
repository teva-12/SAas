namespace Platform.Modules.Identity.Domain;

public enum UserStatus : byte { Active = 1, Disabled = 2 }

public sealed class User
{
    public Guid UserId { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public UserStatus Status { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Only a SHA-256 hash of the refresh token is stored. Tokens rotate on use; a rotated token
/// presented again signals theft and revokes the whole family.</summary>
public sealed class RefreshToken
{
    public Guid RefreshTokenId { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
