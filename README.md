# SaaS Foundation v2 — Tenancy + Identity (Phase 1)

Implements spec 4.1/4.2 (RBAC, tenant-scoped roles), 12.1-12.3 (tenancy), 14.2 (auth endpoints) and 16.1 (security baseline).
**Not compiled or run yet** (no .NET SDK in the authoring sandbox). First steps: `dotnet build` then `dotnet test`, and fix anything the compiler finds.
This zip replaces the earlier `phase1-saas-foundation.zip` (it is a superset).

## What changed since v1
- **Identity module:** register, login, refresh (rotating), logout. Passwords use ASP.NET Core's `PasswordHasher` (adaptive PBKDF2).
  Refresh tokens are stored only as SHA-256 hashes; reuse of a rotated token revokes the whole token family. Lockout after 5 failures for 15 min.
  Login errors are identical for "unknown email" and "wrong password".
- **JWT access tokens (15 min)** carry only the user id. Tenant and role are NOT in the token: they are checked per request against live membership.
- **RBAC:** code-defined roles -> permissions (`Modules/Identity/Application/Permissions.cs`). `RequirePermission("catalog.write")` on endpoints.
- **Tenant onboarding:** `POST /api/v1/tenants` creates a tenant and makes the caller its owner, atomically.
- **Merchant pipeline:** middleware verifies membership, sets tenant + role, THEN authorization runs (order matters, see `Program.cs`).
- **Sample endpoints:** `GET/POST /api/v1/merchant/products` prove tenant isolation + permissions end to end.
- Rate limiting on `/api/v1/auth/*`, ProblemDetails errors, SQL script `002_identity.sql`.

## Run locally
```bash
cd src/Platform.Api
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
dotnet user-secrets set "ConnectionStrings:Platform" "Server=localhost,1433;Database=FashionPlatform;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True"
dotnet ef migrations add Initial && dotnet ef database update   # compare with Database/*.sql
dotnet run
```
The app refuses to start if the signing key is under 32 bytes.

## Try the flow
```bash
H='Content-Type: application/json'; API=http://localhost:5000
curl -X POST $API/api/v1/auth/register -H "$H" -d '{"email":"me@example.com","password":"a-long-password-1"}'
curl -X POST $API/api/v1/auth/login    -H "$H" -d '{"email":"me@example.com","password":"a-long-password-1"}'   # copy accessToken
TOKEN=...
curl -X POST $API/api/v1/tenants -H "$H" -H "Authorization: Bearer $TOKEN" \
  -d '{"name":"Nile Fashion","slug":"nile-fashion","locale":"ar-EG","timeZoneId":"Africa/Cairo","currencyCode":"EGP"}'
curl $API/api/v1/me/tenants -H "Authorization: Bearer $TOKEN"                              # copy tenantId
curl -X POST $API/api/v1/merchant/products -H "$H" -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: <tenantId>" \
  -d '{"name":"Linen Shirt","slug":"linen-shirt"}'
```
Expected: another user's token with the same `X-Tenant-Id` gets 403; a `support_agent` role gets 403 on product writes.

## Decisions to confirm (log as ADRs)
1. GUID primary keys; shared DB with `TenantId` discriminator.
2. Roles are code-defined for MVP (custom merchant roles later = tables).
3. Store managers cannot refund or manage billing yet (owner only) per spec 4.2. Adjust the map in `Permissions.cs` once the product owner decides.
4. Tokens do not embed tenant/role (extra DB lookup per merchant request; cache in Redis later with short TTL and tenant-scoped keys).
5. New tenants start `Active` for now.

## Still missing (security-relevant first)
- Email verification and password reset; MFA (required for platform operators, spec 16.1).
- Audit log for sign-ins, role changes and permission denials (spec 16.1).
- Staff invitation flow (`staff.invite`) and role assignment endpoints.
- Platform-operator identity and time-boxed, audited support access (spec 4.2).
- Per-account rate limits, ForwardedHeaders for the real client IP, CORS allowlist, refresh-token cleanup job.
- Custom-domain verification + TLS, Redis host->tenant cache, OpenAPI document, cursor pagination.
- Tests against real SQL Server (Testcontainers) and HTTP-level tests with WebApplicationFactory (cross-tenant IDOR on every endpoint).
