using Microsoft.EntityFrameworkCore;
using Platform.BuildingBlocks.TenantContext;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Catalog.Domain;

namespace IntegrationTests;

// NOTE: InMemory proves the filter and write-guard logic only. Before launch, repeat these against
// real SQL Server (e.g. Testcontainers) and add IDOR, export, cache-key and file-access cases (spec 12.3).
public class TenantIsolationTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static PlatformDbContext Db(string name, Guid? tenantId)
    {
        var ctx = new TenantContext();
        if (tenantId is { } id) ctx.Set(id, TenantResolutionSource.MerchantMembership);
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseInMemoryDatabase(name).Options;
        return new PlatformDbContext(options, ctx);
    }

    private static Product NewProduct(string slug) => new()
    {
        ProductId = Guid.NewGuid(), Name = slug, Slug = slug,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Insert_is_stamped_with_the_resolved_tenant()
    {
        var name = Guid.NewGuid().ToString();
        await using var a = Db(name, TenantA);
        var p = NewProduct("tee");
        a.Products.Add(p);
        await a.SaveChangesAsync();
        Assert.Equal(TenantA, p.TenantId);
    }

    [Fact]
    public async Task Tenant_only_sees_its_own_rows()
    {
        var name = Guid.NewGuid().ToString();
        await using (var a = Db(name, TenantA)) { a.Products.Add(NewProduct("a-tee")); await a.SaveChangesAsync(); }
        await using (var b = Db(name, TenantB)) { b.Products.Add(NewProduct("b-tee")); await b.SaveChangesAsync(); }

        await using var readA = Db(name, TenantA);
        var seen = await readA.Products.Select(p => p.Slug).ToListAsync();
        Assert.Equal(["a-tee"], seen);
    }

    [Fact]
    public async Task Unresolved_tenant_sees_nothing_and_cannot_write()
    {
        var name = Guid.NewGuid().ToString();
        await using (var a = Db(name, TenantA)) { a.Products.Add(NewProduct("a-tee")); await a.SaveChangesAsync(); }

        await using var none = Db(name, null);
        Assert.Empty(await none.Products.ToListAsync());

        none.Products.Add(NewProduct("x"));
        await Assert.ThrowsAsync<TenantNotResolvedException>(() => none.SaveChangesAsync());
    }

    [Fact]
    public async Task Inserting_a_row_for_another_tenant_is_rejected()
    {
        await using var a = Db(Guid.NewGuid().ToString(), TenantA);
        var p = NewProduct("sneaky");
        p.TenantId = TenantB;
        a.Products.Add(p);
        await Assert.ThrowsAsync<CrossTenantWriteException>(() => a.SaveChangesAsync());
    }

    [Fact]
    public async Task Moving_an_existing_row_to_another_tenant_is_rejected()
    {
        var name = Guid.NewGuid().ToString();
        await using var a = Db(name, TenantA);
        a.Products.Add(NewProduct("tee"));
        await a.SaveChangesAsync();

        var existing = await a.Products.SingleAsync();
        existing.TenantId = TenantB;
        await Assert.ThrowsAsync<CrossTenantWriteException>(() => a.SaveChangesAsync());
    }

    [Fact]
    public void Tenant_context_cannot_be_switched_once_set()
    {
        var ctx = new TenantContext();
        ctx.Set(TenantA, TenantResolutionSource.CustomDomain);
        Assert.Throws<InvalidOperationException>(() => ctx.Set(TenantB, TenantResolutionSource.CustomDomain));
    }
}
