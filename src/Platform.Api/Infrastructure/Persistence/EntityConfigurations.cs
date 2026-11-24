using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Modules.Catalog.Domain;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Infrastructure.Persistence;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants");
        b.HasKey(x => x.TenantId);
        b.Property(x => x.Slug).HasMaxLength(63).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Locale).HasMaxLength(16).IsRequired();
        b.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> b)
    {
        b.ToTable("TenantDomains");
        b.HasKey(x => x.TenantDomainId);
        b.Property(x => x.Domain).HasMaxLength(253).IsRequired();
        b.Property(x => x.VerificationToken).HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.VerifiedAtUtc).HasColumnType("datetime2(3)");
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        // A domain can belong to only one tenant at a time; removed domains free the name up (spec 12.4).
        b.HasIndex(x => x.Domain).IsUnique().HasFilter("[Status] <> 5");
        b.HasIndex(x => new { x.TenantId, x.Status });
    }
}

public sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> b)
    {
        b.ToTable("TenantMemberships");
        b.HasKey(x => x.TenantMembershipId);
        b.Property(x => x.RoleCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Status });
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products");
        b.HasKey(x => x.ProductId);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(220).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc }).IsDescending(false, false, true);
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.UserId);
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.LastLoginAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.LockoutEndUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.HasKey(x => x.RefreshTokenId);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RevokedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion(); // concurrency token: guards parallel refresh with one token
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => x.UserId);
    }
}
