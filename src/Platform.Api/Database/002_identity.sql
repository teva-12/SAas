-- Phase 1b: identity. Run after 001. Passwords are stored only as adaptive hashes (spec 16.1).

CREATE TABLE dbo.Users (
    UserId            uniqueidentifier NOT NULL,
    Email             nvarchar(254)    NOT NULL,
    NormalizedEmail   nvarchar(254)    NOT NULL,
    PasswordHash      nvarchar(256)    NOT NULL,
    Status            tinyint          NOT NULL,   -- 1 Active, 2 Disabled
    FailedLoginCount  int              NOT NULL CONSTRAINT DF_Users_Failed DEFAULT (0),
    LockoutEndUtc     datetime2(3)     NULL,
    CreatedAtUtc      datetime2(3)     NOT NULL,
    LastLoginAtUtc    datetime2(3)     NULL,
    RowVersion        rowversion       NOT NULL,
    CONSTRAINT PK_Users PRIMARY KEY (UserId),
    CONSTRAINT UQ_Users_NormalizedEmail UNIQUE (NormalizedEmail),
    CONSTRAINT CK_Users_Status CHECK (Status IN (1, 2))
);

CREATE TABLE dbo.RefreshTokens (
    RefreshTokenId     uniqueidentifier NOT NULL,
    UserId             uniqueidentifier NOT NULL,
    FamilyId           uniqueidentifier NOT NULL,
    TokenHash          char(64)         NOT NULL,   -- SHA-256 hex of the raw token; raw token is never stored
    CreatedAtUtc       datetime2(3)     NOT NULL,
    ExpiresAtUtc       datetime2(3)     NOT NULL,
    RevokedAtUtc       datetime2(3)     NULL,
    ReplacedByTokenId  uniqueidentifier NULL,
    RowVersion         rowversion       NOT NULL,
    CONSTRAINT PK_RefreshTokens PRIMARY KEY (RefreshTokenId),
    CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId) ON DELETE CASCADE,
    CONSTRAINT UQ_RefreshTokens_Hash UNIQUE (TokenHash)
);
CREATE INDEX IX_RefreshTokens_Family ON dbo.RefreshTokens (FamilyId);
CREATE INDEX IX_RefreshTokens_User ON dbo.RefreshTokens (UserId);
-- Housekeeping TODO: scheduled job purging tokens expired/revoked more than N days ago.

ALTER TABLE dbo.TenantMemberships
    ADD CONSTRAINT FK_TenantMemberships_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId);
