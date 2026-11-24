namespace Platform.Modules.Identity.Application;

/// <summary>Permissions describe an action on a resource (spec 4.1).</summary>
public static class Permissions
{
    public const string CatalogRead = "catalog.read";
    public const string CatalogWrite = "catalog.write";
    public const string OrdersRead = "orders.read";
    public const string OrdersRefund = "orders.refund";
    public const string InventoryAdjust = "inventory.adjust";
    public const string BillingManage = "billing.manage";
    public const string StaffInvite = "staff.invite";

    public static readonly IReadOnlyList<string> All =
        [CatalogRead, CatalogWrite, OrdersRead, OrdersRefund, InventoryAdjust, BillingManage, StaffInvite];
}

/// <summary>
/// Tenant-scoped roles. Code-defined for the MVP; move to tables when merchants need custom roles.
/// TBD with the product owner: refund and billing access for store managers (kept owner-only for now,
/// since spec 4.2 says sensitive actions need explicit permission).
/// </summary>
public static class Roles
{
    public const string Owner = "owner";
    public const string StoreManager = "store_manager";
    public const string CatalogEditor = "catalog_editor";
    public const string InventoryOperator = "inventory_operator";
    public const string SupportAgent = "support_agent";
    public const string Marketing = "marketing";

    private static readonly Dictionary<string, HashSet<string>> Map = new()
    {
        [Owner] = [.. Permissions.All],
        [StoreManager] = [Permissions.CatalogRead, Permissions.CatalogWrite, Permissions.OrdersRead, Permissions.InventoryAdjust],
        [CatalogEditor] = [Permissions.CatalogRead, Permissions.CatalogWrite],
        [InventoryOperator] = [Permissions.CatalogRead, Permissions.InventoryAdjust],
        [SupportAgent] = [Permissions.OrdersRead],
        [Marketing] = [Permissions.CatalogRead],
    };

    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    public static bool IsValid(string role) => Map.ContainsKey(role);

    public static IReadOnlySet<string> PermissionsFor(string role) =>
        Map.TryGetValue(role, out var set) ? set : None;
}
