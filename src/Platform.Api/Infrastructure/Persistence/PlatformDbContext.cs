using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Platform.BuildingBlocks.TenantContext;
using Platform.Modules.Catalog.Domain;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Tenant-scoped context. Query filters are defense in depth (spec 12.3), NOT the only boundary:
/// application services must still require tenant context, and tests must try cross-tenant access.
/// Platform operators need a separate, audited, privileged context (not built yet).
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Fails closed: with no tenant resolved, the filter matches nothing.
    private Guid CurrentTenantId => tenant.TenantId ?? Guid.Empty;

    private static readonly MethodInfo ApplyFilterMethod =
        typeof(PlatformDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            ApplyFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantOwned
        => modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceTenantRules()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;

            var current = tenant.TenantId ?? throw new TenantNotResolvedException();

            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId != Guid.Empty && entry.Entity.TenantId != current)
                    throw new CrossTenantWriteException(
                        $"Attempt to insert {entry.Metadata.ClrType.Name} for another tenant.");
                entry.Entity.TenantId = current; // stamped from trusted context, never from the client
                continue;
            }

            var original = entry.Property(e => e.TenantId).OriginalValue;
            if (original != current || entry.Entity.TenantId != original)
                throw new CrossTenantWriteException(
                    $"Attempt to modify or delete {entry.Metadata.ClrType.Name} outside the current tenant.");
        }
    }
}
