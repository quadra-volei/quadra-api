# Spec: Login flows

> ## Amendment — 2026-10-05: own JWT, Twilio Verify, login creates the user (supersedes the Cognito details below)
>
> Johny decided to drop AWS Cognito. Where this amendment and the original text disagree, the amendment wins.
>
> **Model**
> - No separate signup (FA.2 is superseded). The first valid SMS OTP for a phone number, or the first valid Google ID token for a Google account, creates the `users` row and publishes `UserRegistered`; every interactive login publishes `UserLoggedIn`.
> - The API issues its own session: an HS256 JWT access token (`sub` = `users.id`, default 15 min) and an opaque random refresh token (default 30 days), stored only as a SHA-256 hash in `refresh_tokens`.
> - SMS OTP goes through `IPhoneVerificationService`: `TwilioVerifyPhoneVerificationService` (Twilio Verify v2 REST via `HttpClient`, no new NuGet) or `FakePhoneVerificationService` (fixed code, no SMS, refused in Production). Selected by `Auth:PhoneVerification:Provider` (`Twilio` | `Fake`). The code is 6 digits; the provider owns generation, expiry and attempt limits.
> - Google ID tokens are validated by the existing `GoogleAppleTokenValidator` (`aud` = `Auth:Google:ClientId`, the **web** OAuth client ID the mobile app passes as `webClientId`). The Apple endpoint is kept but answers 401 until `Auth:Apple:ClientId` is configured.
>
> **Endpoints** (all under `/api/v1/auth`)
> - `POST /login/sms-otp` — `{ step: "initiate" | "verify", phoneNumber, code?, deviceId? }` (no `session` field).
>   - initiate → `200 { delivery: { deliveryMedium, deliveryDestination } }` for **any** valid E.164 number (no 404 for unknown phones; calling it again resends).
>   - verify → `200 AuthTokensResponse`; `401` wrong or expired code.
>   - `400` validation, `422` number refused by the SMS provider, `429` provider throttling, `502` provider failure.
> - `POST /login/google`, `POST /login/apple` — `{ idToken, deviceId? }` → `200 AuthTokensResponse`; `401` invalid token; `400` validation.
> - `POST /refresh` — `{ refreshToken, deviceId? }` → `200 AuthTokensResponse` with a **new** refresh token; the presented one is revoked atomically (two concurrent refreshes of one token: exactly one succeeds). `401` unknown / revoked / expired.
> - `POST /logout` — `{ refreshToken }` → `204` always (revokes the token if it was active).
> - `GET /me` — bearer token required → `200 { userId, provider, phoneNumber, email }`; `401` without a valid token or when the user no longer exists.
>
> `AuthTokensResponse`: `{ accessToken, refreshToken, tokenType: "Bearer", expiresIn, userId, isNewUser }` (no `idToken`, `cognitoSub` or `confirmationStatus`).
>
> **Database** — migration `ReplaceCognitoWithOwnIdentity`: drops `users.cognito_sub`, `users.confirmation_status`, `refresh_tokens.cognito_sub` and their indexes; adds `users.external_subject varchar(255) NULL` (the Google/Apple `sub`) with a unique index on `(provider, external_subject)` where not null; deletes existing `refresh_tokens` rows (Cognito-issued sessions cannot be refreshed).
>
> **Events** — `UserRegistered(UserId, Provider, PhoneNumber, Email, OccurredAt)` and `UserLoggedIn(UserId, Provider, DeviceId, OccurredAt)`; the `CognitoSub` and `ConfirmationStatus` fields were removed.
>
> **Configuration** — `Auth:Jwt` (`Issuer`, `Audience`, `SigningKey`, `AccessTokenMinutes`, `RefreshTokenDays`, `ClockSkewSeconds`), `Auth:PhoneVerification` (`Provider`, `Twilio:{AccountSid, AuthToken, VerifyServiceSid}`, `Fake:{Code, PhoneNumber}`), `Auth:Google:ClientId`. Secrets come from the environment / secret store, never from versioned files.
>
> Everything below is the original Cognito-based spec, kept for history.

## Origin
- User Story / Feature from SCOPE: FA.3 — Login flows
- Layer: Cross-cutting (Auth, MVP)
- Requested by: renanortega.dev@gmail.com

## Goal
Allow an already-provisioned user to obtain a usable Cognito JWT (access + ID + refresh tokens) through phone SMS OTP, Google, or Apple, and to renew that session via a refresh token — without ever using the Cognito Hosted UI.

## Primary module
Quadra.Modules.Auth

## Dependent modules (read-only via interface)
- None. Login is a self-contained Auth concern. The tokens it issues are consumed by every other module only later, through the FA.1 JWT middleware and `HttpContext.User` — never via a direct call during login.

## Scope interpretation note
SCOPE.md FA.3 lists four endpoints: `POST /auth/login/sms-otp` (initiate + verify), `POST /auth/login/google`, `POST /auth/login/apple`, and `POST /auth/refresh`. All four issue a JWT (and refresh token) by exchanging an upstream credential for a Cognito session. This spec follows the same server-side, native-SDK approach established by FA.2 (no Hosted UI redirect): the mobile client holds the provider credential, POSTs it here, and the server brokers the Cognito session.

Consistency anchors taken from the existing module source:
- Reuses `CognitoSignupOptions` (`Auth:Cognito` — `AppClientId`, optional `AppClientSecret`, `UserPoolId`, `Region`) and `OidcProvidersOptions` (`Auth:Google`, `Auth:Apple`) already bound and fail-fast validated in `AuthModuleExtensions`.
- Reuses `IOidcTokenValidator` / `OidcClaims` / `OidcProvider` for Google and Apple token validation (already JWKS-backed, no new NuGet).
- Reuses `IAmazonCognitoIdentityProvider`, `SecretHashCalculator`, and the existing `users` table (`User`, `IUserRepository`, `UserConfirmationStatus`).
- Reuses the single `AuthController` (`[Route("api/v1/auth")]`) — adds new actions, does not create a second controller.
- Refresh tokens are persisted in the `refresh_tokens` table, which `docs/ARCHITECTURE.md` already assigns to the Auth module and which FA.1/FA.2 explicitly deferred to FA.3.

The "sms-otp initiate" step corresponds to Cognito `InitiateAuth`/`SignUpAsync`-driven code delivery; the "verify" step to `RespondToAuthChallenge` or `ConfirmSignUp` + `InitiateAuth`. SCOPE says one endpoint with two operations, so this spec exposes a single `POST /auth/login/sms-otp` discriminated by a `step` field.

## Database changes

### New tables
- `refresh_tokens`
  - `id` `uuid` `PRIMARY KEY DEFAULT gen_random_uuid()`
  - `user_id` `uuid` `NOT NULL` — FK to `users.id` (same module, FK allowed). The local user this session belongs to.
  - `token_hash` `varchar(128)` `NOT NULL UNIQUE` — SHA-256 hash (hex) of the Cognito refresh token. The raw refresh token is never stored — only its hash, so a DB leak cannot replay sessions.
  - `cognito_sub` `varchar(64)` `NOT NULL` — denormalized from `users.cognito_sub` to allow revocation lookups without a join.
  - `device_id` `varchar(128)` `NULL` — opaque client-supplied device identifier, lets a user have multiple concurrent sessions and lets a future logout target one device. Nullable for clients that do not send it.
  - `issued_at` `timestamptz` `NOT NULL DEFAULT now()`
  - `expires_at` `timestamptz` `NOT NULL` — copied from the Cognito refresh-token validity (configurable on the App Client).
  - `revoked_at` `timestamptz` `NULL` — set when the token is rotated or explicitly revoked; non-null means unusable.
  - Indexes:
    - unique on `token_hash`
    - btree on `user_id`
    - btree on `cognito_sub`
    - btree on `expires_at` (for cleanup queries)

### New columns in existing tables
- None. `users.confirmation_status` already exists and is transitioned (`Unconfirmed → Confirmed`) by the SMS OTP verify step; no schema change required.

### Migrations required
- `<TimestampPrefix>_CreateRefreshTokensTable` — generated under `src/Quadra.Modules.Auth/Migrations/` (EF generates the rollback).

## REST endpoints

### `POST /api/v1/auth/login/sms-otp`
- **Auth**: public (`[AllowAnonymous]`) — login is how a user *gets* a token.
- **Request** (C# record — discriminated by `Step`):
  ```csharp
  public record SmsOtpLoginRequest(
      string Step,              // "initiate" | "verify"
      string PhoneNumber,       // E.164, required for both steps
      string? Code,             // 6-digit OTP, required when Step == "verify"
      string? Session,          // opaque Cognito challenge session from the initiate step
      string? DeviceId);        // optional opaque device identifier
  ```
- **Response 2xx**:
  - For `Step == "initiate"` (`200 OK`):
    ```csharp
    public record SmsOtpInitiateResponse(
        string Session,                  // opaque Cognito session to echo back on verify
        SmsDeliveryDetails Delivery);    // reuses Contracts.SmsDeliveryDetails from FA.2
    ```
  - For `Step == "verify"` (`200 OK`): returns `AuthTokensResponse` (see shared response below).
- **Possible errors**:
  - `400` — validation failure (bad E.164, `verify` missing `Code`/`Session`, unknown `Step`).
  - `401` — wrong/expired OTP code, or the challenge session is invalid/expired.
  - `404` — no local `users` row exists for the phone number (user must complete FA.2 signup first).
  - `429` — Cognito returns `TooManyRequestsException` / `LimitExceededException` (code request throttled).
  - `502` — unexpected Cognito service error.
- **Validation** (FluentValidation, `SmsOtpLoginRequestValidator`):
  - `Step`: required; one of `initiate`, `verify` (case-insensitive, normalized lowercase).
  - `PhoneNumber`: required; matches E.164 regex `^\+[1-9]\d{1,14}$`.
  - When `Step == "verify"`: `Code` required, exactly 6 digits `^\d{6}$`; `Session` required, non-empty, max 4096 chars.
  - `DeviceId`: optional; max 128 chars when present.
- **Behavior**:
  1. Validate via FluentValidation; on failure return `400`.
  2. **initiate**:
     a. Look up the local `users` row by phone number; if absent return `404`.
     b. Start a custom/OTP auth challenge via Cognito `InitiateAuthAsync` (or `AdminInitiateAuthAsync`) with `AuthFlow = CUSTOM_AUTH` (or `USER_PASSWORD_AUTH` driving an SMS MFA challenge, per pool config). Compute `SecretHash` if the App Client has a secret.
     c. Cognito delivers the SMS and returns a `Session` and `CodeDeliveryDetails`. Return `200` with the opaque session and masked delivery destination.
  3. **verify**:
     a. Echo the `Session` and `Code` to Cognito via `RespondToAuthChallengeAsync` (challenge `SMS_MFA` / `CUSTOM_CHALLENGE`).
     b. On success Cognito returns `AuthenticationResult` (AccessToken, IdToken, RefreshToken, ExpiresIn).
     c. If the local user is `Unconfirmed`, flip `confirmation_status` to `Confirmed` and bump `updated_at` (first successful OTP confirms the phone account, completing the FA.2 phone signup).
     d. Persist the refresh token (hash) and return tokens (shared persist + response steps below).
     e. On wrong/expired code return `401`.

### `POST /api/v1/auth/login/google`
- **Auth**: public (`[AllowAnonymous]`).
- **Request** (C# record):
  ```csharp
  public record OidcLoginRequest(
      string IdToken,           // provider ID token from the native SDK
      string? DeviceId);
  ```
- **Response 2xx** (`200 OK`): `AuthTokensResponse`.
- **Possible errors**:
  - `400` — validation failure (empty/oversized token).
  - `401` — the Google ID token fails signature/issuer/audience/expiry validation.
  - `404` — token is valid but no Cognito user / local `users` row is linked to this Google `sub` (user must complete FA.2 signup first).
  - `502` — unexpected Cognito service error.
- **Validation** (FluentValidation, `OidcLoginRequestValidator`):
  - `IdToken`: required, non-empty, max 8192 chars.
  - `DeviceId`: optional, max 128 chars.
- **Behavior**:
  1. Validate; on failure return `400`.
  2. Validate the ID token via `IOidcTokenValidator.ValidateAsync(idToken, OidcProvider.Google, ct)`. On failure return `401`.
  3. Resolve the Cognito user for the external `sub` via the Cognito client (`AdminInitiateAuthAsync` / token-broker, or look up the linked user); if no linked user exists return `404`.
  4. Obtain a Cognito session for that user (server-side broker — see implementation notes). Persist the refresh token and return tokens (shared steps below).

### `POST /api/v1/auth/login/apple`
- Identical contract and behavior to `POST /auth/login/google`, but validates the token with `OidcProvider.Apple` (issuer `https://appleid.apple.com`, audience `Auth:Apple:ClientId`). Same `OidcLoginRequest` / `AuthTokensResponse` shapes and same error table.

### `POST /api/v1/auth/refresh`
- **Auth**: public (`[AllowAnonymous]`) — a refresh token is the credential; no access token required.
- **Request** (C# record):
  ```csharp
  public record RefreshRequest(
      string RefreshToken,
      string? DeviceId);
  ```
- **Response 2xx** (`200 OK`): `AuthTokensResponse`. The refresh leg returns a fresh access + ID token; the returned `RefreshToken` is the same one unless rotation is enabled (see notes).
- **Possible errors**:
  - `400` — validation failure (empty token).
  - `401` — refresh token is unknown locally, revoked, expired, or rejected by Cognito.
  - `502` — unexpected Cognito service error.
- **Validation** (FluentValidation, `RefreshRequestValidator`):
  - `RefreshToken`: required, non-empty, max 4096 chars.
  - `DeviceId`: optional, max 128 chars.
- **Behavior**:
  1. Validate; on failure return `400`.
  2. Hash the supplied refresh token (SHA-256 hex) and look up the `refresh_tokens` row by `token_hash`. If absent, revoked (`revoked_at` not null), or expired (`expires_at` in the past) → `401`.
  3. Call Cognito `InitiateAuthAsync` with `AuthFlow = REFRESH_TOKEN_AUTH`, supplying the raw refresh token (and `SecretHash` if the App Client has a secret).
  4. On Cognito success: return the new access + ID token. If rotation is enabled and Cognito returns a new refresh token, mark the old row `revoked_at = now()` and persist a new `refresh_tokens` row.
  5. On Cognito rejection (`NotAuthorizedException`): mark the local row revoked and return `401`.

### Shared response contract
```csharp
public record AuthTokensResponse(
    string AccessToken,
    string IdToken,
    string RefreshToken,
    string TokenType,         // always "Bearer"
    int ExpiresIn,            // access-token lifetime in seconds, from Cognito
    Guid UserId,              // local users.id
    string CognitoSub,
    string ConfirmationStatus); // "Confirmed" after a successful login
```

### Shared persist + response steps (used by sms-otp verify, google, apple, refresh)
1. From the Cognito `AuthenticationResult`, read AccessToken, IdToken, RefreshToken, ExpiresIn.
2. Resolve the local `users` row by `cognito_sub` (`IUserRepository.FindByCognitoSubAsync`).
3. Insert a `refresh_tokens` row: `token_hash = SHA256(refreshToken)`, `user_id`, `cognito_sub`, `device_id`, `issued_at = now()`, `expires_at = now() + refreshTokenValidity`. Persist via a dedicated `IRefreshTokenRepository`.
4. Return `200 OK` with `AuthTokensResponse`. The raw refresh token is returned to the client exactly once per issuance and is never logged.

## SQS events

### Published
- `UserLoggedIn` — fired after a successful token issuance on any login endpoint (not on `refresh`).
  - Payload:
    ```csharp
    public record UserLoggedIn(
        Guid UserId,
        string CognitoSub,
        string Provider,        // "phone" | "google" | "apple"
        string? DeviceId,
        DateTimeOffset OccurredAt);
    ```
  - Declared consumer (in MVP): `Quadra.Modules.Profile` — uses last-login as a freshness signal for the player profile / "active player" derivation owned by F2.1. The Profile-side handler is owned by the F2.1 spec. (Published via the existing `IEventPublisher`; no new infra.)

### Consumed
- None.

## Real-time (if applicable)

### Hubs / Methods
- N/A — login and refresh are plain HTTP operations with no SignalR surface. (The FA.1 middleware already governs how issued tokens authenticate future `/hubs/*` connections.)

## Files to create
- `src/Quadra.Modules.Auth/Entities/RefreshToken.cs` — EF entity (class) for `refresh_tokens`; private parameterless ctor + factory `RefreshToken.Issue(...)` and method `Revoke(DateTimeOffset now)`.
- `src/Quadra.Modules.Auth/Persistence/RefreshTokenConfiguration.cs` — `IEntityTypeConfiguration<RefreshToken>` (table name, indexes, snake_case columns, FK to `users`).
- `src/Quadra.Modules.Auth/Persistence/IRefreshTokenRepository.cs` — `FindByTokenHashAsync`, `AddAsync`, `RevokeAsync` — all with `CancellationToken`.
- `src/Quadra.Modules.Auth/Persistence/RefreshTokenRepository.cs` — EF implementation; scoped lifetime.
- `src/Quadra.Modules.Auth/Cognito/ICognitoAuthClient.cs` — internal abstraction: `InitiateSmsOtpAsync`, `RespondToSmsOtpAsync`, `BrokerExternalSessionAsync`, `RefreshAsync`. Returns a common `CognitoAuthResult`.
- `src/Quadra.Modules.Auth/Cognito/CognitoAuthClient.cs` — wraps `IAmazonCognitoIdentityProvider`; translates SDK exceptions into application-layer login exceptions; reuses `SecretHashCalculator`.
- `src/Quadra.Modules.Auth/Cognito/CognitoAuthResults.cs` — records `SmsOtpChallenge(Session, DeliveryMedium, DeliveryDestination)` and `CognitoAuthResult(AccessToken, IdToken, RefreshToken, ExpiresIn, RefreshTokenExpiresAt, CognitoSub)`.
- `src/Quadra.Modules.Auth/Application/LoginExceptions.cs` — `InvalidOtpException`, `OtpChallengeExpiredException`, `UserNotFoundForLoginException`, `RefreshTokenRejectedException`, `CognitoAuthThrottledException` (reuses existing `OidcTokenInvalidException`, `CognitoUnavailableException` from `SignupExceptions.cs`).
- `src/Quadra.Modules.Auth/Application/SmsOtpLoginHandler.cs` — orchestrates initiate/verify, confirmation flip, persist + publish.
- `src/Quadra.Modules.Auth/Application/OidcLoginHandler.cs` — orchestrates Google/Apple validate → broker → persist + publish.
- `src/Quadra.Modules.Auth/Application/RefreshHandler.cs` — orchestrates lookup → Cognito refresh → rotation.
- `src/Quadra.Modules.Auth/Application/RefreshTokenHasher.cs` — SHA-256 hex helper (no new NuGet; `System.Security.Cryptography`).
- `src/Quadra.Modules.Auth/Contracts/SmsOtpLoginRequest.cs` — request DTO.
- `src/Quadra.Modules.Auth/Contracts/SmsOtpInitiateResponse.cs` — initiate response DTO (reuses `SmsDeliveryDetails`).
- `src/Quadra.Modules.Auth/Contracts/OidcLoginRequest.cs` — request DTO (shared by google + apple).
- `src/Quadra.Modules.Auth/Contracts/RefreshRequest.cs` — request DTO.
- `src/Quadra.Modules.Auth/Contracts/AuthTokensResponse.cs` — shared response DTO.
- `src/Quadra.Modules.Auth/Validation/SmsOtpLoginRequestValidator.cs`
- `src/Quadra.Modules.Auth/Validation/OidcLoginRequestValidator.cs`
- `src/Quadra.Modules.Auth/Validation/RefreshRequestValidator.cs`
- `src/Quadra.Shared/Events/Auth/UserLoggedIn.cs` — shared event contract.
- `tests/Quadra.UnitTests/Modules/Auth/SmsOtpLoginRequestValidatorTests.cs` — scaffolding.
- `tests/Quadra.UnitTests/Modules/Auth/OidcLoginRequestValidatorTests.cs` — scaffolding.
- `tests/Quadra.UnitTests/Modules/Auth/RefreshHandlerTests.cs` — scaffolding.
- `tests/Quadra.IntegrationTests/Auth/LoginEndpointsTests.cs` — scaffolding.

## Files to modify
- `src/Quadra.Modules.Auth/Controllers/AuthController.cs` — add four actions (`LoginSmsOtp`, `LoginGoogle`, `LoginApple`, `Refresh`); inject the three handlers and their validators; map the new exceptions to status codes. Keep the controller thin (no business logic). Remove the "FA.3 endpoints will be added later" comment.
- `src/Quadra.Modules.Auth/Persistence/AuthDbContext.cs` — add `DbSet<RefreshToken> RefreshTokens`; apply `RefreshTokenConfiguration`.
- `src/Quadra.Modules.Auth/DependencyInjection/AuthModuleExtensions.cs` — register `IRefreshTokenRepository`, `ICognitoAuthClient`, the three handlers, and the three new validators inside `AddSignupPipeline` (or a renamed `AddAuthPipeline`). No new options sections — reuses `CognitoSignupOptions` and `OidcProvidersOptions`.
- `src/Quadra.Api/appsettings.json` — add `Aws:Sqs:UserLoggedInQueueUrl` placeholder under the existing AWS section. (No new `Auth` keys — refresh-token validity comes from the Cognito App Client config.)

## Acceptance criteria (from SCOPE)
- [ ] endpoint POST /api/v1/auth/login/sms-otp (initiate + verify)
- [ ] endpoint POST /api/v1/auth/login/google (exchange Google token for Cognito session)
- [ ] endpoint POST /api/v1/auth/login/apple (idem)
- [ ] endpoint POST /api/v1/auth/refresh
- [ ] returns JWT + refresh token
- [ ] OUT: account linking (multiple providers same user) — push to v2
- [ ] OUT: password reset (Cognito-hosted flow only, not custom)

## Out of scope (be explicit)
- Account linking across providers (attaching Google to a phone account, etc.) — explicitly OUT per SCOPE, push to v2.
- Custom password reset / forgot-password endpoints — Cognito-hosted only, not exposed here.
- Logout / token revocation endpoints — not in SCOPE FA.3 (the `revoked_at` column is provisioned for rotation, but no `POST /auth/logout` is added in this spec).
- Email/password login — this product has no password login path.
- Signup / user provisioning — owned by FA.2. Login assumes the user already exists; absence yields `404`.
- JWT validation / claims projection — owned by FA.1.
- MFA enrollment management, TOTP, or remembering devices beyond storing an opaque `device_id`.
- CAPTCHA / bot protection and rate limiting on login endpoints (separate cross-cutting spec when needed; the `429` mapping only surfaces Cognito's own throttling).
- Cognito User Pool / App Client provisioning (infrastructure-as-code).
- Cleanup job for expired `refresh_tokens` rows — a Background Worker scheduled job in a later spec (`expires_at` index is provisioned for it).
- Provisioning the `UserLoggedIn` consumer on the Profile side (owned by F2.1).

## New NuGet dependencies
- NONE. All four endpoints reuse `AWSSDK.CognitoIdentityProvider`, `AWSSDK.Extensions.NETCore.Setup`, `FluentValidation`, the EF/Npgsql stack, and `Microsoft.IdentityModel.*` already referenced by `Quadra.Modules.Auth.csproj` (FA.1/FA.2). Refresh-token hashing uses the BCL `System.Security.Cryptography`. SQS publishing uses the existing `IEventPublisher` abstraction in `Quadra.Infrastructure`.

## Implementation notes
- **Single configuration surface**: reuse `CognitoSignupOptions` (`Auth:Cognito`) for `AppClientId`/`AppClientSecret`/`UserPoolId`/`Region` and `OidcProvidersOptions` for Google/Apple. Do not introduce a parallel options class.
- **SecretHash**: when the App Client has a secret, every Cognito auth call (`InitiateAuth`, `RespondToAuthChallenge`, `REFRESH_TOKEN_AUTH`) needs a `SecretHash` computed via the existing `SecretHashCalculator`. For `REFRESH_TOKEN_AUTH` the username component is the Cognito `sub`/username, not the refresh token.
- **External-provider session brokering**: FA.2 links the Google/Apple identity into Cognito via `AdminLinkProviderForUser`. To mint a session from a validated provider token without the Hosted UI, the implementer uses an admin-initiated flow (`AdminInitiateAuthAsync` with a `CUSTOM_AUTH` flow, or an admin-set transient secret) to obtain Cognito tokens for the linked user. **Decision needed during implementation**: confirm the chosen Cognito flow with the human if the pool is not configured for admin-initiated custom auth; do not invent a new external provider. This mirrors FA.2's server-side, no-redirect stance.
- **Refresh token at rest**: only the SHA-256 hash (hex) is stored in `token_hash`. The raw token is returned to the client and never persisted, never logged. On `refresh`, hash the input and compare.
- **Rotation**: if the App Client has refresh-token rotation enabled, Cognito returns a new refresh token on `REFRESH_TOKEN_AUTH`; revoke the old `refresh_tokens` row (`revoked_at = now()`) and insert the new one in the same transaction. If rotation is disabled, return the same token and leave the row untouched.
- **Confirmation flip**: a successful SMS OTP `verify` is the moment a phone-signup user (`Unconfirmed` from FA.2) becomes `Confirmed`. Update `users.confirmation_status` and `updated_at` using the injected `TimeProvider` in the same transaction that persists the refresh token.
- **Event after commit**: publish `UserLoggedIn` only after the DB transaction commits (at-least-once, idempotent consumers), matching FA.2's `UserRegistered` ordering. Use the existing `IEventPublisher`.
- **Exception → status mapping** in the controller (consistent with FA.2's pattern): `OidcTokenInvalidException`/`InvalidOtpException`/`OtpChallengeExpiredException`/`RefreshTokenRejectedException` → `401`; `UserNotFoundForLoginException` → `404`; `CognitoAuthThrottledException` → `429`; `CognitoUnavailableException` → `502`. Cognito errors must not leak as 500s.
- **Async/CancellationToken**: every public handler/repository/Cognito-client method takes a `CancellationToken` propagated from `HttpContext.RequestAborted`. No `.Result`/`.Wait()`.
- **Nullable reference types**: enabled. Nullable fields are `device_id`, `revoked_at` on the entity and `Code`/`Session`/`DeviceId` on requests.
- **Controller thinness**: actions only validate, invoke the handler, and map exceptions. No business logic.
- **Module boundary**: only `Quadra.Modules.Auth` references the Cognito SDK and OIDC validators. The `refresh_tokens` FK to `users` is intra-module and allowed. Other modules learn of logins solely via the `UserLoggedIn` event in `Quadra.Shared`.
- **Time source**: use the injected `TimeProvider.System` (already registered) for `issued_at`, `expires_at`, `revoked_at`, `updated_at`, and event `OccurredAt`.
- **Serilog**: log login attempts with structured fields `Provider`, `Step` (sms-otp), and `OutcomeCategory` (`success | invalid_otp | otp_expired | oidc_invalid | user_not_found | refresh_rejected | throttled | cognito_unavailable`). Never log raw OTP codes, ID tokens, refresh tokens, or full phone numbers (mask middle digits, reusing FA.2's masking).
