# Task - task_002_be_jwt_token_service

## Requirement Reference

- User Story: us_005
- Story Location: .propel/context/tasks/EP-TECH/us_005/us_005.md
- Acceptance Criteria:
  - AC-1: Given Identity is configured, When a user authenticates with valid credentials, Then the system issues a JWT access token (15-minute expiry) and a refresh token (7-day expiry, HttpOnly cookie).
  - AC-2: Given a JWT token is issued, When the token is decoded, Then it contains user ID, email, and role claims (Patient, Staff, or Admin).
  - AC-3: Given an access token has expired, When a request includes a valid refresh token, Then the system issues a new access token without requiring re-authentication.
  - AC-4: Given a user logs out, When the logout endpoint is called, Then the refresh token is invalidated in Redis blacklist and the token is no longer usable.
- Edge Case:
  - What happens when the JWT signing key is rotated? Old tokens remain valid until expiry; new tokens use the new key. Key rotation is logged.
  - How does the system handle an expired refresh token? Returns 401 Unauthorized with message "Session expired. Please log in again."

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Authentication | ASP.NET Core Identity + JWT Bearer | 8.x |
| Token Library | Microsoft.AspNetCore.Authentication.JwtBearer | 8.x |
| Caching | Upstash Redis (via ICacheService) | 7.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the JWT token issuance, validation, and refresh lifecycle. Create a token service that generates JWT access tokens (15-minute expiry) containing user ID, email, and role claims, and refresh tokens (7-day expiry) stored as HttpOnly secure cookies. Implement a refresh endpoint that issues new access tokens from valid refresh tokens without re-authentication. Implement a logout endpoint that blacklists refresh tokens in Redis to prevent reuse. Configure JWT Bearer authentication middleware to validate access tokens on all protected endpoints.

## Dependent Tasks

- task_001_be_identity_configuration — Identity with ApplicationUser, roles, and bcrypt must be configured before token issuance can reference user claims.
- US_004 task_002_be_cache_service_layer — ICacheService with Redis must be available for refresh token blacklisting on logout.

## Impacted Components

- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add Microsoft.AspNetCore.Authentication.JwtBearer NuGet package
- **NEW** `src/UPACIP.Service/Auth/ITokenService.cs` — Token service interface for JWT generation, validation, and refresh
- **NEW** `src/UPACIP.Service/Auth/TokenService.cs` — JWT token generation with claims, refresh token creation, Redis blacklist integration
- **NEW** `src/UPACIP.Service/Auth/JwtSettings.cs` — Configuration POCO for JWT settings (issuer, audience, signing key, expiry)
- **NEW** `src/UPACIP.Api/Controllers/AuthController.cs` — Login, refresh, and logout API endpoints
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register JWT Bearer authentication, ITokenService, and JWT configuration binding
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add JwtSettings configuration section

## Implementation Plan

1. **Install NuGet packages**: Add `Microsoft.AspNetCore.Authentication.JwtBearer` (8.x) to the Api project and `System.IdentityModel.Tokens.Jwt` (if not transitively included) to the Service project for token generation.
2. **Create JwtSettings POCO**: Define a configuration class in the Service project with properties: `Issuer` (string), `Audience` (string), `SigningKey` (string — loaded from secrets), `AccessTokenExpiryMinutes` (int, default 15), `RefreshTokenExpiryDays` (int, default 7). Bind from `appsettings.json` section `JwtSettings`.
3. **Create ITokenService interface**: Define methods: `GenerateAccessToken(ApplicationUser user, IList<string> roles)` returning JWT string, `GenerateRefreshToken()` returning a cryptographically random token string, `GetPrincipalFromExpiredToken(string token)` returning `ClaimsPrincipal` for refresh flow, `BlacklistRefreshTokenAsync(string token)` for logout, `IsRefreshTokenBlacklistedAsync(string token)` for validation.
4. **Implement TokenService**: Generate JWT access tokens using `JwtSecurityTokenHandler` with claims: `sub` (user ID), `email`, `role` (from Identity roles), `jti` (unique token ID). Sign with `HmacSha256` using the signing key from configuration. Generate refresh tokens using `RandomNumberGenerator.GetBytes(64)` encoded as Base64Url. For blacklisting, store revoked refresh token hashes in Redis via `ICacheService` with TTL matching the refresh token's remaining validity.
5. **Create AuthController**: Implement three endpoints:
   - `POST /api/auth/login` — Validate credentials via `SignInManager`, generate access + refresh tokens, return access token in response body and refresh token as HttpOnly secure SameSite cookie.
   - `POST /api/auth/refresh` — Read refresh token from HttpOnly cookie, validate it's not blacklisted, extract principal from expired access token, issue new access + refresh token pair.
   - `POST /api/auth/logout` — Read refresh token from cookie, blacklist it in Redis, clear the cookie.
6. **Configure JWT Bearer middleware**: In `Program.cs`, register `AddAuthentication` with default scheme `JwtBearerDefaults.AuthenticationScheme`, then `AddJwtBearer` with `TokenValidationParameters`: validate issuer, audience, signing key, require expiration, and clock skew of zero. Add `UseAuthentication()` and `UseAuthorization()` to the middleware pipeline in the correct position.
7. **Add JWT signing key to configuration**: Add `JwtSettings` section to `appsettings.json` with placeholder values. Store the actual signing key in user secrets (`dotnet user-secrets set "JwtSettings:SigningKey" "{key}"`) — never hardcode the signing key. The key must be at least 256 bits (32+ characters) for HMAC-SHA256.
8. **Handle key rotation edge case**: Document in configuration comments that old tokens remain valid until their expiry when the signing key changes. No dual-key validation is needed for Phase 1 — key rotation requires redeployment and old tokens expire within 15 minutes.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs (with Identity, Redis, EF Core registered)
│   │   ├── appsettings.json
│   │   ├── Security/
│   │   │   └── BcryptPasswordHasher.cs
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs
│   │   │   └── CorrelationIdMiddleware.cs
│   │   └── Controllers/
│   │       └── WeatherForecastController.cs
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   └── Caching/
│   │       ├── ICacheService.cs
│   │       └── RedisCacheService.cs
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── ApplicationUser.cs
│       │   └── ApplicationRole.cs
│       └── Migrations/
└── scripts/
    └── ...
```

> Assumes task_001_be_identity_configuration and US_004 tasks are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add `Microsoft.AspNetCore.Authentication.JwtBearer` 8.x NuGet package |
| CREATE | src/UPACIP.Service/Auth/JwtSettings.cs | Configuration POCO: Issuer, Audience, SigningKey, AccessTokenExpiryMinutes (15), RefreshTokenExpiryDays (7) |
| CREATE | src/UPACIP.Service/Auth/ITokenService.cs | Interface: GenerateAccessToken, GenerateRefreshToken, GetPrincipalFromExpiredToken, BlacklistRefreshTokenAsync, IsRefreshTokenBlacklistedAsync |
| CREATE | src/UPACIP.Service/Auth/TokenService.cs | JWT generation with sub/email/role/jti claims, refresh token with RandomNumberGenerator, Redis blacklist via ICacheService |
| CREATE | src/UPACIP.Api/Controllers/AuthController.cs | POST /api/auth/login, POST /api/auth/refresh, POST /api/auth/logout endpoints |
| MODIFY | src/UPACIP.Api/Program.cs | Register AddAuthentication + AddJwtBearer with TokenValidationParameters, bind JwtSettings, register ITokenService, add UseAuthentication + UseAuthorization |
| MODIFY | src/UPACIP.Api/appsettings.json | Add JwtSettings section (Issuer, Audience, AccessTokenExpiryMinutes: 15, RefreshTokenExpiryDays: 7) |

## External References

- [ASP.NET Core JWT Bearer authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-8.0)
- [JWT token creation in .NET](https://learn.microsoft.com/en-us/dotnet/api/system.identitymodel.tokens.jwt.jwtsecuritytokenhandler)
- [ASP.NET Core authentication middleware](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/?view=aspnetcore-8.0)
- [Refresh tokens best practices (OWASP)](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html#token-sidejacking)
- [HttpOnly cookie security](https://owasp.org/www-community/HttpOnly)

## Build Commands

```powershell
# Restore and build
dotnet restore UPACIP.sln
dotnet build UPACIP.sln --configuration Debug

# Set JWT signing key in user secrets
dotnet user-secrets init --project src/UPACIP.Api
dotnet user-secrets set "JwtSettings:SigningKey" "YourSecure256BitKeyHereAtLeast32Chars!" --project src/UPACIP.Api

# Run the API
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Test login endpoint
curl -X POST https://localhost:{port}/api/auth/login -H "Content-Type: application/json" -d '{"email":"test@example.com","password":"Test123!"}'

# Test refresh (cookie auto-sent)
curl -X POST https://localhost:{port}/api/auth/refresh --cookie "refreshToken={token}"

# Test logout
curl -X POST https://localhost:{port}/api/auth/logout --cookie "refreshToken={token}"
```

## Implementation Validation Strategy

- [x] `dotnet build` completes with zero errors and zero warnings
- [x] `POST /api/auth/login` with valid credentials returns a JWT access token in the response body
- [x] `POST /api/auth/login` sets a refresh token as an HttpOnly, Secure, SameSite=Strict cookie
- [x] Decoded JWT contains `sub` (user ID), `email`, `role`, and `jti` claims
- [x] Access token expires after 15 minutes (verify with token decode)
- [x] `POST /api/auth/refresh` with valid refresh token cookie returns new access token without re-authentication
- [x] `POST /api/auth/refresh` with expired refresh token returns 401 with "Session expired. Please log in again."
- [x] `POST /api/auth/logout` blacklists refresh token — subsequent refresh attempt returns 401
- [x] JWT signing key is not hardcoded in appsettings.json (loaded from user secrets or environment variable)

## Implementation Checklist

- [x] Add `Microsoft.AspNetCore.Authentication.JwtBearer` 8.x NuGet package to `UPACIP.Api.csproj`
- [x] Create `src/UPACIP.Service/Auth/JwtSettings.cs` with Issuer, Audience, SigningKey, AccessTokenExpiryMinutes (15), RefreshTokenExpiryDays (7)
- [x] Create `src/UPACIP.Service/Auth/ITokenService.cs` and `TokenService.cs` with JWT generation (sub, email, role, jti claims, HmacSha256), refresh token generation (RandomNumberGenerator 64 bytes), and Redis blacklist integration via ICacheService
- [x] Create `src/UPACIP.Api/Controllers/AuthController.cs` with login (credential validation via SignInManager, token issuance), refresh (cookie read, blacklist check, new token pair), and logout (blacklist in Redis, clear cookie) endpoints
- [x] Register `AddAuthentication` + `AddJwtBearer` in `Program.cs` with `TokenValidationParameters` (validate issuer, audience, signing key, expiration, zero clock skew) and bind `JwtSettings` from configuration
- [x] Add `UseAuthentication()` and `UseAuthorization()` to the middleware pipeline after CORS and before endpoint mapping
- [x] Add `JwtSettings` section to `appsettings.json` and store the signing key in user secrets (minimum 256-bit key for HMAC-SHA256)
