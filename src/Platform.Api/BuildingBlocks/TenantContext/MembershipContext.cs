namespace Platform.BuildingBlocks.TenantContext;

/// <summary>The caller's role inside the resolved tenant (set only after membership is verified).</summary>
public interface IMembershipContext
{
    string? RoleCode { get; }
}

public interface IMembershipContextSetter
{
    void Set(string roleCode);
}

public sealed class MembershipContext : IMembershipContext, IMembershipContextSetter
{
    public string? RoleCode { get; private set; }
    public void Set(string roleCode) => RoleCode = roleCode;
}
