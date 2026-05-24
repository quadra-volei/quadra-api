# Spec: JWT Validation Middleware

## Origin
- User Story / Feature from SCOPE: FA.1 — JWT Validation Middleware
- Layer: Cross-cutting (Auth, MVP)
- Requested by: renanortega.dev@gmail.com

## Goal
Enable every protected route in the API to require and trust a valid AWS Cognito-issued JWT before any module-level code runs.

## Primary module
Quadra.Modules.Auth

## Dependent modules (read-only via interface)
- None. This feature is a foundational cross-cutting concern. It is consumed by every other module's controllers through the standard `[Authorize]` attribute and `HttpContext.User`.

## Database changes

### New tables
- None. Token validation is stateless and relies solely on Cognito's public JWKS.

### New columns in existing tables
- None.

### Migrations required
- None.

## REST endpoints

This feature does not introduce new endpoints. It installs middleware on the ASP.NET Core pipeline that affects every existing and future endpoint. Endpoint authorization is expressed at the controller/action level via `[Authorize]` and `[AllowAnonymous]`.

Behavior introduced on every request:

1. The JWT bearer middleware reads the `Authorization: Bearer <token>` header.
2. If the header is absent on a `[Authorize]`-protected route → respond `401 Unauthorized`.
3. If the token is malformed, signature-invalid, expired, has wrong issuer, or wrong audience → respond `401 Unauthorized` with `WWW-Authenticate: Bearer error="invalid_token"`.
4. If the token is valid, claims are projected onto `HttpContext.User` (a `ClaimsPrincipal`), exposing at least:
   - `sub` (Cognito user id) — mapped to `ClaimTypes.NameIdentifier`
   - `email`
   - `custom:role` — mapped to `ClaimTypes.Role` so `[Authorize(Roles = "...")]` works
   - `cognito:username`
5. Routes decorated `[AllowAnonymous]` (e.g. `/health`, future `auth/signup`, `auth/login/*`) bypass token requirements but still parse the token if present.

Standard 401 error body returned for protected routes lacking a valid token:
```csharp
public record UnauthorizedErrorResponse(string Error, string Message);
// { "error": "unauthorized", "message": "A valid bearer token is required." }
```

## SQS events

### Published
- None.

### Consumed
- None.

## Real-time (if applicable)

The same validation must apply to SignalR connections. The bearer token is read from the `access_token` query string parameter on `/hubs/*` paths (SignalR convention). Invalid tokens result in the WebSocket handshake being rejected with `401`.

### Hubs / Methods
- N/A — no new hubs. Behavior applies to any future hub under `/hubs/*`.

## Files to create
- `src/Quadra.Modules.Auth/DependencyInjection/AuthModuleExtensions.cs` — exposes `AddAuthModule(IServiceCollection, IConfiguration)` (registers JWT bearer + options) and `UseAuthModule(IApplicationBuilder)` (wires `UseAuthentication` + `UseAuthorization` in the right order).
- `src/Quadra.Modules.Auth/Configuration/CognitoJwtOptions.cs` — strongly-typed options bound to `Auth:Cognito` (UserPoolId, Region, Audience, optional ClockSkewSeconds).
- `src/Quadra.Modules.Auth/Configuration/CognitoJwtOptionsValidator.cs` — FluentValidation validator that runs at startup and fails fast if required values are missing.
- `src/Quadra.Modules.Auth/Authentication/CognitoJwtBearerEventsHandler.cs` — `JwtBearerEvents` subclass that (a) reads `access_token` from the query string for SignalR paths, (b) writes the standardized 401 JSON body on `OnChallenge`, (c) logs structured failures via Serilog.
- `src/Quadra.Modules.Auth/Authentication/CognitoClaimsTransformer.cs` — `IClaimsTransformation` that normalizes Cognito claims (`sub` → `NameIdentifier`, `custom:role` → `Role`).
- `tests/Quadra.UnitTests/Modules/Auth/CognitoJwtOptionsValidatorTests.cs` — validation tests (test scaffolding only; test-writer agent owns the rest).

## Files to modify
- `src/Quadra.Api/Program.cs` — call `builder.Services.AddAuthModule(builder.Configuration)`; call `app.UseAuthModule()` between routing and endpoints; mark `/health` with `.AllowAnonymous()`.
- `src/Quadra.Api/appsettings.json` — add empty `Auth:Cognito` section with keys (`UserPoolId`, `Region`, `Audience`) so configuration shape is documented.
- `src/Quadra.Api/appsettings.Development.json` — add development placeholder values (kept empty; the developer fills them locally).
- `src/Quadra.Modules.Auth/Quadra.Modules.Auth.csproj` — add `Microsoft.AspNetCore.Authentication.JwtBearer` package reference and `FrameworkReference` to `Microsoft.AspNetCore.App` if not already present.

## Acceptance criteria (from SCOPE)
- [ ] ASP.NET middleware that validates JWTs issued by AWS Cognito
- [ ] Extracts claims (sub, email, custom:role) into HttpContext.User
- [ ] Rejects expired or malformed tokens with 401
- [ ] Configuration via appsettings (Cognito User Pool ID, region, audience)
- [ ] OUT: token issuance (Cognito does that)
- [ ] OUT: refresh token logic (FA.3)
- [ ] OUT: any UI for login (frontend)

## Out of scope (be explicit)
- Token issuance, signup, login flows, refresh tokens (those are FA.2 and FA.3).
- `users` and `refresh_tokens` tables — they are owned by Auth but provisioned by FA.2/FA.3, not this spec.
- Any controller actions under `/api/v1/auth/*`.
- Role/permission seeding or a role catalog — only the plumbing that exposes `custom:role` as `ClaimTypes.Role` is in scope.
- Authorization policies beyond default `[Authorize]`. No `AddAuthorization(options => ...)` policy definitions in this spec.
- Caching of Cognito JWKS beyond what the JWT bearer middleware does natively (it already caches and refreshes signing keys).
- Audit logging of authentication attempts (only standard Serilog failure logs).
- Cross-cutting CORS, rate limiting or anti-forgery configuration.

## New NuGet dependencies
- `Microsoft.AspNetCore.Authentication.JwtBearer@10.0.*` — required to validate Cognito-signed JWTs against a JWKS endpoint. This is the first-party ASP.NET package and the canonical way to implement JWT validation; no alternative inside the locked stack.

## Implementation notes
- **Authority**: `https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}`. The JWT bearer middleware will auto-discover the OIDC config (`.well-known/openid-configuration`) and JWKS from this URL — do NOT hardcode the JWKS URL.
- **Audience**: Cognito Access Tokens do not carry an `aud` claim — they carry `client_id`. If access tokens are used, configure `TokenValidationParameters.ValidateAudience = false` and instead validate `client_id` via a custom check in `OnTokenValidated`. If ID tokens are used, `Audience` matches the Cognito App Client ID. **Decision needed during implementation**: the spec expects validation of `client_id` against the configured `Audience` value to keep a single configuration surface; implementer must follow this pattern.
- **Issuer validation**: `ValidateIssuer = true`, issuer must equal the authority URL above.
- **Clock skew**: default to `TimeSpan.FromSeconds(30)`; expose as `ClockSkewSeconds` option for tests.
- **SignalR token extraction**: in `OnMessageReceived`, if `context.Request.Path` starts with `/hubs` and a query-string `access_token` is present, copy it to `context.Token`. This is the official ASP.NET pattern for WebSocket auth.
- **Failure response**: override `OnChallenge` to suppress the default empty body and write `application/json` with the `UnauthorizedErrorResponse` shape so the client always sees a consistent error contract.
- **Async/CancellationToken**: all event handlers must be async and propagate `context.HttpContext.RequestAborted`.
- **Nullable reference types**: options class properties for `UserPoolId`, `Region`, `Audience` are non-nullable `string` and validated at startup.
- **Module boundary**: no other module references `Microsoft.AspNetCore.Authentication.JwtBearer`. Only `Quadra.Modules.Auth` does. Other modules consume the result purely via `[Authorize]` and `HttpContext.User`.
- **Testability**: `CognitoJwtOptionsValidator` can be unit-tested without a host; full middleware behavior belongs to the integration test suite using `WebApplicationFactory` with a fake JWT signing key (test-writer's responsibility).
- **Local development**: when `Auth:Cognito:UserPoolId` is empty in Development, fail fast at startup with a clear message — no silent "auth disabled" mode (avoids accidental insecure deploys).
