namespace Platform.Modules.Tenancy.Domain;

public enum TenantStatus : byte { Pending = 0, Active = 1, Suspended = 2, Closed = 3 }
public enum TenantDomainStatus : byte { Pending = 0, Verified = 1, Provisioning = 2, Active = 3, Failed = 4, Removed = 5 }
public enum MembershipStatus : byte { Invited = 0, Active = 1, Suspended = 2, Removed = 3 }

// Control-plane tables: deliberately NOT ITenantOwned, because they are read
// to *discover* the tenant before any tenant context exists.

public sealed class Tenant
{
    public Guid TenantId { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public TenantStatus Status { get; set; }
    public required string Locale { get; set; }        // e.g. "ar-EG"; no country default hard-coded
    public required string TimeZoneId { get; set; }
    public required string CurrencyCode { get; set; }  // ISO 4217
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TenantDomain
{
    public Guid TenantDomainId { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string Domain { get; set; }       // lowercase, no port
    public TenantDomainStatus Status { get; set; }
    public required string VerificationToken { get; set; } // DNS TXT challenge value
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
}

public sealed class TenantMembership
{
    public Guid TenantMembershipId { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid UserId { get; set; }                   // FK added when the Identity module lands
    public required string RoleCode { get; set; }      // TBD: align with roles in spec section 4
    public MembershipStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
