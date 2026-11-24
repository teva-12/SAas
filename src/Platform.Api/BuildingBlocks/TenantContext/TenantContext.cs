namespace Platform.BuildingBlocks.TenantContext;

public enum TenantResolutionSource : byte
{
    None = 0,
    PlatformSubdomain = 1,
    CustomDomain = 2,
    MerchantMembership = 3,
    TrustedBackgroundJob = 4
}

/// <summary>Read-only view of the tenant the current request/job runs for.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    bool IsResolved { get; }
    TenantResolutionSource Source { get; }
}

/// <summary>Only resolution middleware and trusted job runners may call this. Never feed it client input directly.</summary>
public interface ITenantContextSetter
{
    void Set(Guid tenantId, TenantResolutionSource source);
}

public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    public Guid? TenantId { get; private set; }
    public bool IsResolved => TenantId.HasValue;
    public TenantResolutionSource Source { get; private set; }

    public void Set(Guid tenantId, TenantResolutionSource source)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        if (TenantId.HasValue && TenantId != tenantId)
            throw new InvalidOperationException("Tenant context is already set for this scope and cannot be changed.");
        TenantId = tenantId;
        Source = source;
    }
}

/// <summary>Marker for rows owned by a tenant. Gets a global query filter and write guards.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public sealed class TenantNotResolvedException()
    : InvalidOperationException("No tenant is resolved for this operation.");

public sealed class CrossTenantWriteException(string message) : InvalidOperationException(message);
