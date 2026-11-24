-- Phase 1 baseline for SQL Server 2022. Reference DDL; EF Core migrations should be the deployed source of truth.
-- Conventions per spec 13.1: GUID public keys, datetime2(3) UTC, rowversion, explicit constraints.

CREATE TABLE dbo.Tenants (
    TenantId      uniqueidentifier NOT NULL,
    Slug          nvarchar(63)     NOT NULL,
    Name          nvarchar(200)    NOT NULL,
    Status        tinyint          NOT NULL,
    Locale        nvarchar(16)     NOT NULL,
    TimeZoneId    nvarchar(64)     NOT NULL,
    CurrencyCode  char(3)          NOT NULL,
    CreatedAtUtc  datetime2(3)     NOT NULL,
    UpdatedAtUtc  datetime2(3)     NOT NULL,
    RowVersion    rowversion       NOT NULL,
    CONSTRAINT PK_Tenants PRIMARY KEY (TenantId),
    CONSTRAINT UQ_Tenants_Slug UNIQUE (Slug),
    CONSTRAINT CK_Tenants_Status CHECK (Status BETWEEN 0 AND 3),
    CONSTRAINT CK_Tenants_Slug CHECK (LEN(Slug) >= 3 AND Slug NOT LIKE '%[^a-z0-9-]%')
);

CREATE TABLE dbo.TenantDomains (
    TenantDomainId     uniqueidentifier NOT NULL,
    TenantId           uniqueidentifier NOT NULL,
    Domain             nvarchar(253)    NOT NULL,
    Status             tinyint          NOT NULL,   -- 0 Pending,1 Verified,2 Provisioning,3 Active,4 Failed,5 Removed
    VerificationToken  nvarchar(100)    NOT NULL,
    CreatedAtUtc       datetime2(3)     NOT NULL,
    VerifiedAtUtc      datetime2(3)     NULL,
    CONSTRAINT PK_TenantDomains PRIMARY KEY (TenantDomainId),
    CONSTRAINT FK_TenantDomains_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (TenantId),
    CONSTRAINT CK_TenantDomains_Status CHECK (Status BETWEEN 0 AND 5),
    CONSTRAINT CK_TenantDomains_Lowercase CHECK (Domain = LOWER(Domain))
);
-- A domain belongs to one tenant at a time; removed rows release the name.
CREATE UNIQUE INDEX UX_TenantDomains_Domain_Live ON dbo.TenantDomains (Domain) WHERE Status <> 5;
CREATE INDEX IX_TenantDomains_Tenant_Status ON dbo.TenantDomains (TenantId, Status);

CREATE TABLE dbo.TenantMemberships (
    TenantMembershipId uniqueidentifier NOT NULL,
    TenantId           uniqueidentifier NOT NULL,
    UserId             uniqueidentifier NOT NULL,   -- FK to Users once the Identity module exists
    RoleCode           nvarchar(50)     NOT NULL,
    Status             tinyint          NOT NULL,   -- 0 Invited,1 Active,2 Suspended,3 Removed
    CreatedAtUtc       datetime2(3)     NOT NULL,
    CONSTRAINT PK_TenantMemberships PRIMARY KEY (TenantMembershipId),
    CONSTRAINT FK_TenantMemberships_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (TenantId),
    CONSTRAINT UQ_TenantMemberships_Tenant_User UNIQUE (TenantId, UserId),
    CONSTRAINT CK_TenantMemberships_Status CHECK (Status BETWEEN 0 AND 3)
);
CREATE INDEX IX_TenantMemberships_User_Status ON dbo.TenantMemberships (UserId, Status);

CREATE TABLE dbo.Products (
    ProductId     uniqueidentifier NOT NULL,
    TenantId      uniqueidentifier NOT NULL,
    Name          nvarchar(200)    NOT NULL,
    Slug          nvarchar(220)    NOT NULL,
    Description   nvarchar(max)    NULL,
    Status        tinyint          NOT NULL,
    CreatedAtUtc  datetime2(3)     NOT NULL,
    UpdatedAtUtc  datetime2(3)     NOT NULL,
    RowVersion    rowversion       NOT NULL,
    CONSTRAINT PK_Products PRIMARY KEY (ProductId),
    CONSTRAINT FK_Products_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (TenantId),
    CONSTRAINT UQ_Products_Tenant_Slug UNIQUE (TenantId, Slug),
    CONSTRAINT CK_Products_Status CHECK (Status BETWEEN 0 AND 2)
);
CREATE INDEX IX_Products_Tenant_Status_Created ON dbo.Products (TenantId, Status, CreatedAtUtc DESC);
