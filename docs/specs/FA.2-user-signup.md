# Spec: User Signup

## Origin
- User Story / Feature from SCOPE: FA.2 — User Signup
- Layer: Cross-cutting (Auth, MVP)
- Requested by: renanortega.dev@gmail.com

## Goal
Allow a new person to create an account in Quadra via one of three identity providers (phone SMS OTP, Google, or Apple), persisting the corresponding local user record that every other module will reference.

## Primary module
Quadra.Modules.Auth

## Dependent modules (read-only via interface)
- None. Signup is the first touchpoint in the system; downstream modules (Profile, Matches, etc.) consume the resulting `users.id` only after authenticated requests via the `UserRegistered` event, not during signup.

## Scope interpretation note
SCOPE.md FA.2 lists one endpoint (`POST /api/v1/auth/signup`) while the product supports three identity providers (phone, Google, Apple — confirmed by SCOPE FA.3 login endpoints and `docs/ARCHITECTURE.md` Cognito row). To honor the SCOPE wording literally, this spec exposes a **single endpoint** that accepts a `provider` discriminator with a provider-specific payload.

- **Phone**: invokes Cognito `SignUpAsync` with username = E.164 phone, a server-generated cryptographically-random password (never returned to the client; the user never types it — Cognito uses it only as an internal account secret), and the `phone_number` attribute. Cognito delivers the SMS OTP. OTP verification is **NOT** part of this endpoint — it belongs to FA.3 (`/auth/login/sms-otp`), per SCOPE.
- **Google / Apple**: the client obtains an ID token from the provider's native SDK and POSTs it to this endpoint. The server validates the token against the provider's JWKS (Google: `https://accounts.google.com/.well-known/openid-configuration`; Apple: `https://appleid.apple.com/.well-known/openid-configuration`), then provisions the user in Cognito via `AdminCreateUserAsync` with `MessageAction = SUPPRESS`, marks the account confirmed via `AdminConfirmSignUpAsync` (or sets `email_verified`/`phone_verified` attributes), and links the external IdP identity via `AdminLinkProviderForUserAsync`. This server-side path avoids the Cognito Hosted UI redirect (no browser flow), which is required because the MVP is backend-only and the mobile client uses native SDKs. Token exchange for JWTs themselves occurs in FA.3's login endpoints.

## Database changes

### New tables
- `users`
  - `id` `uuid` `PRIMARY KEY DEFAULT gen_random_uuid()`
  - `cognito_sub` `varchar(64)` `NOT NULL UNIQUE` — Cognito user `sub` claim, the durable external identifier and the bridge to every JWT. Required by SCOPE.
  - `provider` `varchar(16)` `NOT NULL` — enum-as-string: `phone | google | apple`. Records which identity provider was used at signup. Needed because the same user cannot mix providers in MVP (account linking is explicitly OUT per SCOPE FA.3) and because downstream modules and support tooling need to know how the account was created.
  - `phone_number` `varchar(20)` `NULL` — E.164 format. Required when `provider = phone`; populated from Google/Apple token only if the provider returns a verified phone (rare; usually null). Justified because phone is the primary login key for the SMS OTP flow.
  - `email` `varchar(320)` `NULL` — RFC 5321 maximum length; stored lowercased. `NULL` is allowed because Apple "Hide My Email" may return a private relay address only when the user opts in, and a phone-signup user may never provide an email. Not unique-constrained (see indexes) to accommodate Apple private relays that can shift.
  - `confirmation_status` `varchar(32)` `NOT NULL` — enum-as-string: `Unconfirmed | Confirmed`. For `provider = phone`, starts as `Unconfirmed` and is flipped to `Confirmed` by the FA.3 OTP verification (out of scope here, schema supports it). For `provider = google | apple`, starts as `Confirmed` because the ID token is verified server-side before insertion.
  - `created_at` `timestamptz` `NOT NULL DEFAULT now()` — required by SCOPE.
  - `updated_at` `timestamptz` `NOT NULL DEFAULT now()` — needed for `confirmation_status` transitions and future profile-side updates routed through Auth.
  - Indexes:
    - unique on `cognito_sub`
    - unique partial index on `phone_number WHERE phone_number IS NOT NULL`
    - non-unique btree on `email`
    - btree on `created_at`

### New columns in existing tables
- None.

### Migrations required
- `<TimestampPrefix>_CreateUsersTable` — generated under `src/Quadra.Modules.Auth/Migrations/` (EF generates the rollback).

## REST endpoints

### `POST /api/v1/auth/signup`
- **Auth**: public (`[AllowAnonymous]`) — this endpoint is how a user *gets* a token, so it cannot itself require one.
- **Request** (C# record — polymorphic via `provider` discriminator):
  ```csharp
  public record SignupRequest(
      string Provider,             // "phone" | "google" | "apple"
      PhoneSignupPayload? Phone,
      OidcSignupPayload? Google,
      OidcSignupPayload? Apple);

  public record PhoneSignupPayload(string PhoneNumber); // E.164

  public record OidcSignupPayload(string IdToken);
  ```
- **Response 2xx** (`201 Created`):
  ```csharp
  public record SignupResponse(
      Guid UserId,
      string CognitoSub,
      string Provider,
      string ConfirmationStatus,        // "Unconfirmed" | "Confirmed"
      SmsDeliveryDetails? SmsDelivery); // populated only when Provider == "phone"

  public record SmsDeliveryDetails(
      string DeliveryMedium,            // always "SMS" in MVP
      string DeliveryDestination);      // masked phone, e.g. "+55********90"
  ```
- **Possible errors**:
  - `400` — request fails validation (missing payload for declared provider, malformed E.164 phone, missing ID token).
  - `401` — the supplied Google/Apple ID token fails signature/issuer/audience/expiry validation.
  - `409` — `cognito_sub` already exists locally, or the same `phone_number` already exists locally, or Cognito returns `UsernameExistsException`/`AliasExistsException`.
  - `422` — Cognito returns `InvalidParameterException`, `CodeDeliveryFailureException` (SMS could not be initiated for phone signup), or the IdP token is valid but missing required claims (e.g. `sub`).
  - `502` — unexpected Cognito service error (mapped from `AmazonCognitoIdentityProviderException` other than the cases above).
- **Validation** (FluentValidation, `SignupRequestValidator`):
  - `Provider`: required; must be one of `phone`, `google`, `apple` (case-insensitive, normalized to lowercase).
  - When `Provider == "phone"`: `Phone` payload required; `Google` and `Apple` must be null; `Phone.PhoneNumber` must match E.164 regex `^\+[1-9]\d{1,14}$`.
  - When `Provider == "google"`: `Google` payload required; `Phone` and `Apple` must be null; `Google.IdToken` non-empty, max 8192 chars.
  - When `Provider == "apple"`: `Apple` payload required; `Phone` and `Google` must be null; `Apple.IdToken` non-empty, max 8192 chars.
- **Behavior**:
  1. Validate the request via FluentValidation; on failure return `400`.
  2. Dispatch on `Provider`:
     - **phone**:
       a. Pre-check `users` table: if a row with the same `phone_number` exists, return `409` without calling Cognito.
       b. Generate a cryptographically-random throwaway password (≥ 16 chars, satisfies the pool policy) — never returned, never logged.
       c. Call Cognito `SignUpAsync` with `Username = phoneNumber`, generated password, attribute `phone_number = phoneNumber`. Compute `SecretHash` if the App Client has a secret.
       d. Cognito returns `UserSub` and `CodeDeliveryDetails` (SMS).
       e. Set `confirmation_status = "Unconfirmed"`; capture masked delivery destination for the response.
     - **google** / **apple**:
       a. Validate the ID token against the provider's JWKS:
          - Google: issuer `https://accounts.google.com` or `accounts.google.com`; audience equal to configured `Auth:Google:ClientId`; signature via JWKS; `exp` not in past.
          - Apple: issuer `https://appleid.apple.com`; audience equal to configured `Auth:Apple:ClientId`; signature via JWKS; `exp` not in past; `nonce` is not enforced server-side in MVP (mobile SDK responsibility).
          - On any validation failure, return `401`.
       b. Extract `sub` (mandatory) and `email` (optional) claims from the token. For Apple, also honor `email_verified` claim.
       c. Pre-check `users` table by `cognito_sub` derived from a deterministic lookup: query Cognito `AdminListUsers` with `filter = "identities.providerName = \"<Google|SignInWithApple>\" and identities.userId = \"<sub>\""`. If found, return `409`.
       d. Call Cognito `AdminCreateUserAsync` with `Username = Guid.NewGuid().ToString()`, `MessageAction = SUPPRESS`, attributes including `email` (if known), `email_verified = true` (if known). 
       e. Call Cognito `AdminLinkProviderForUserAsync` linking the destination user to the external IdP (`Google` or `SignInWithApple`) using the external `sub` as `ProviderAttributeValue` and `ProviderAttributeName = Cognito_Subject`.
       f. Call Cognito `AdminConfirmSignUpAsync` (or rely on suppressed creation already producing `CONFIRMED`) to ensure `confirmation_status = "Confirmed"`.
       g. Capture the Cognito-side `sub` of the destination user from the `AdminCreateUser` response.
  3. Translate Cognito exceptions to HTTP responses as listed under "Possible errors". Cognito errors must not leak as 500s.
  4. Insert a `users` row in a single transaction: `id = Guid.NewGuid()`, `cognito_sub = UserSub`, `provider = normalized provider`, `phone_number = phone-or-null`, `email = lowercased-email-or-null`, `confirmation_status` per dispatch result.
  5. If the insert fails on a unique constraint (`23505`) for `cognito_sub` or `phone_number` (race), return `409`.
  6. Publish `UserRegistered` to SQS (see below) **after** the DB commit, never before — at-least-once semantics, idempotent consumers expected.
  7. Return `201 Created` with `Location: /api/v1/auth/users/{userId}` header and the `SignupResponse` body. `SmsDelivery` is populated only for the phone provider.

## SQS events

### Published
- `UserRegistered` — fired after a `users` row is committed.
  - Payload:
    ```csharp
    public record UserRegistered(
        Guid UserId,
        string CognitoSub,
        string Provider,             // "phone" | "google" | "apple"
        string? PhoneNumber,
        string? Email,
        string ConfirmationStatus,
        DateTimeOffset OccurredAt);
    ```
  - Declared consumer (in MVP): `Quadra.Modules.Profile` — listens to bootstrap an empty `player_profiles` row tied to `UserId`. The Profile-side handler is owned by the F2.1 spec.

### Consumed
- None.

## Real-time (if applicable)

### Hubs / Methods
- N/A — signup is a plain HTTP operation and has no SignalR surface.

## Files to create
- `src/Quadra.Modules.Auth/Entities/User.cs` — EF entity (class) for the `users` table; private parameterless ctor and factory `User.CreateForPhone(...)`, `User.CreateForExternal(...)`.
- `src/Quadra.Modules.Auth/Entities/IdentityProvider.cs` — enum `Phone | Google | Apple`, persisted as lowercase string via EF value converter.
- `src/Quadra.Modules.Auth/Entities/UserConfirmationStatus.cs` — enum `Unconfirmed | Confirmed`, persisted as string.
- `src/Quadra.Modules.Auth/Persistence/UserConfiguration.cs` — `IEntityTypeConfiguration<User>` mapping table name, indexes (including the partial unique on `phone_number`), value converters, snake_case columns.
- `src/Quadra.Modules.Auth/Persistence/IUserRepository.cs` — methods `FindByCognitoSubAsync`, `FindByPhoneNumberAsync`, `AddAsync` — all with `CancellationToken`.
- `src/Quadra.Modules.Auth/Persistence/UserRepository.cs` — EF implementation; scoped lifetime.
- `src/Quadra.Modules.Auth/Cognito/ICognitoSignupClient.cs` — internal abstraction with three operations: `SignUpPhoneAsync`, `ProvisionExternalUserAsync`, `LinkExternalIdentityAsync`.
- `src/Quadra.Modules.Auth/Cognito/CognitoSignupClient.cs` — implementation wrapping `IAmazonCognitoIdentityProvider`; translates SDK exceptions into application-layer exceptions.
- `src/Quadra.Modules.Auth/Cognito/CognitoSignupResults.cs` — records for `PhoneSignupResult(UserSub, DeliveryMedium, DeliveryDestination)` and `ExternalSignupResult(UserSub)`.
- `src/Quadra.Modules.Auth/Cognito/SecretHashCalculator.cs` — HMAC-SHA256 helper for App Clients that have a secret.
- `src/Quadra.Modules.Auth/Oidc/IOidcTokenValidator.cs` — abstraction `Task<OidcClaims> ValidateAsync(string idToken, OidcProvider provider, CancellationToken ct)`.
- `src/Quadra.Modules.Auth/Oidc/GoogleAppleTokenValidator.cs` — JWKS-backed validator using `Microsoft.IdentityModel.Tokens` (already a transitive dep of FA.1's JWT middleware — no new NuGet).
- `src/Quadra.Modules.Auth/Application/SignupCommand.cs` — discriminated record encapsulating validated input.
- `src/Quadra.Modules.Auth/Application/SignupHandler.cs` — orchestrates dispatch → Cognito → DB insert → event publish.
- `src/Quadra.Modules.Auth/Application/SignupExceptions.cs` — `PhoneAlreadyRegisteredException`, `ExternalIdentityAlreadyLinkedException`, `OidcTokenInvalidException`, `CognitoPolicyViolationException`, `CognitoUnavailableException`.
- `src/Quadra.Modules.Auth/Contracts/SignupRequest.cs` — public DTO record (and nested payload records).
- `src/Quadra.Modules.Auth/Contracts/SignupResponse.cs` — public DTO record (and `SmsDeliveryDetails`).
- `src/Quadra.Modules.Auth/Validation/SignupRequestValidator.cs` — FluentValidation validator with conditional rules per `Provider`.
- `src/Quadra.Modules.Auth/Controllers/AuthController.cs` — thin controller exposing `POST /api/v1/auth/signup`; resolves the handler and maps exceptions to HTTP responses. (If the file already exists, add only the `Signup` action.)
- `src/Quadra.Shared/Events/Auth/UserRegistered.cs` — shared event contract.
- `tests/Quadra.UnitTests/Modules/Auth/SignupRequestValidatorTests.cs` — scaffolding for the test-writer.
- `tests/Quadra.UnitTests/Modules/Auth/SignupHandlerTests.cs` — scaffolding for the test-writer.
- `tests/Quadra.UnitTests/Modules/Auth/GoogleAppleTokenValidatorTests.cs` — scaffolding.
- `tests/Quadra.IntegrationTests/Auth/SignupEndpointTests.cs` — scaffolding for the test-writer.

## Files to modify
- `src/Quadra.Modules.Auth/DependencyInjection/AuthModuleExtensions.cs` — register `IUserRepository`, `ICognitoSignupClient`, `IOidcTokenValidator`, `SignupHandler`, `IAmazonCognitoIdentityProvider` (via AWS SDK extension); bind new options classes.
- `src/Quadra.Modules.Auth/Configuration/CognitoSignupOptions.cs` — new options class bound to `Auth:Cognito` for `AppClientId`, optional `AppClientSecret`, `UserPoolId` (already used by FA.1). Reuses `Region`.
- `src/Quadra.Modules.Auth/Configuration/OidcProvidersOptions.cs` — bound to `Auth:Google` and `Auth:Apple` (each carrying `ClientId`, `Issuer`, `JwksUri`).
- `src/Quadra.Modules.Auth/Persistence/AuthDbContext.cs` — add `DbSet<User> Users`; apply `UserConfiguration`. (File created here if it does not exist yet.)
- `src/Quadra.Api/Program.cs` — no change beyond ensuring `AddAuthModule` is invoked (already done in FA.1). Controllers are auto-discovered.
- `src/Quadra.Api/appsettings.json` — add `Auth:Cognito:AppClientId`, `Auth:Google:ClientId`, `Auth:Apple:ClientId` placeholders; add `Aws:Sqs:UserRegisteredQueueUrl` placeholder under the existing AWS section.
- `src/Quadra.Modules.Auth/Quadra.Modules.Auth.csproj` — add NuGet references listed below.

## Acceptance criteria (from SCOPE)
- [ ] endpoint POST /api/v1/auth/signup that triggers Cognito SignUp
- [ ] persists local user record (id, cognito_sub, email, created_at) in `users` table
- [ ] returns user id and confirmation status
- [ ] OUT: email/SMS confirmation flow (handled by Cognito)
- [ ] OUT: profile data (that's the Profile module's F2.1)

## Out of scope (be explicit)
- SMS OTP verification endpoint — belongs to FA.3 (`/auth/login/sms-otp`).
- Login, refresh tokens, JWT issuance — all FA.3.
- Account linking across providers (e.g. attaching Google to a phone account) — explicitly OUT per SCOPE FA.3.
- Email/password signup of any kind — does not exist in this product.
- Email verification, email-based password reset, magic links.
- Resending the SMS OTP code (FA.3 concern).
- Creating a `player_profiles` row directly from this module — `Profile` module owns its table and reacts to `UserRegistered`.
- Cognito User Pool provisioning (infrastructure-as-code, not application code).
- CAPTCHA / bot protection on the signup endpoint.
- Rate limiting (a separate cross-cutting spec when needed).
- Soft-delete or account deletion endpoints.
- Roles assignment (`custom:role`) — handled outside signup.
- Backfill or migration of existing Cognito users into the new table.
- Apple "Hide My Email" relay rotation handling.

## New NuGet dependencies
- `AWSSDK.CognitoIdentityProvider@latest stable` — required to call Cognito's `SignUp`, `AdminCreateUser`, `AdminLinkProviderForUser`, `AdminConfirmSignUp`, `AdminListUsers` server-side. Canonical SDK package for the locked AWS stack.
- `AWSSDK.Extensions.NETCore.Setup@latest stable` — wires the AWS SDK into the standard .NET DI/config pipeline.
- No new NuGet for OIDC token validation: `Microsoft.IdentityModel.Tokens` and `Microsoft.IdentityModel.Protocols.OpenIdConnect` are already pulled in by the JWT middleware from FA.1 and are reused here.
- (SQS publishing: assumed already provided by `Quadra.Infrastructure` per architecture; no new NuGet inside Auth for SQS.)

## Implementation notes
- **Provider exclusivity in MVP**: a single phone or single external identity per `users` row. The unique partial index on `phone_number` and uniqueness on `cognito_sub` enforce this at the DB layer. Re-signup with the same Google/Apple `sub` is detected via `AdminListUsers` lookup and rejected with `409`.
- **Phone-signup throwaway password**: generated with `RandomNumberGenerator`, ≥ 16 chars including symbol/digit/upper/lower to satisfy any reasonable Cognito policy. Never returned, never logged. The Cognito account is unlocked only via SMS OTP in FA.3.
- **Token validation hardening**: cache JWKS with a 1-hour absolute expiration (use `ConfigurationManager<OpenIdConnectConfiguration>` from the existing IdentityModel libraries). Validate `iss`, `aud`, `exp`, `iat`, signature. Reject tokens older than `15 minutes` (`iat` skew check) to limit replay.
- **Apple specifics**: Apple may omit `email` on subsequent sign-ins; this endpoint is signup-only, so `email` (if present) is captured once. `email_verified` from Apple is treated as authoritative.
- **Cognito ordering**: we call Cognito first, then insert locally. If Cognito succeeds but the DB insert fails, the Cognito user becomes orphaned. Acceptable for MVP — log at `Error` level with the Cognito `UserSub` for manual reconciliation. A future spec may add a reconciliation worker.
- **Event after commit**: `UserRegistered` is published only after the EF transaction commits. Use the `IEventPublisher` from `Quadra.Infrastructure`.
- **Idempotency**: not required at endpoint level for MVP. Duplicate POSTs naturally collapse via `phone_number` and `cognito_sub` constraints. For Google/Apple, the `AdminListUsers` pre-check is the primary dedup.
- **Async/CancellationToken**: every public method on the handler, repository, Cognito client, and OIDC validator takes a `CancellationToken` propagated from `HttpContext.RequestAborted`.
- **Nullable reference types**: enabled. `phone_number` and `email` are the nullable fields on the entity.
- **Controller thinness**: `AuthController.Signup` only triggers FluentValidation, invokes the handler, and maps known exceptions to status codes. No business logic in the controller.
- **Configuration fail-fast**: `Auth:Cognito:AppClientId`, `Auth:Cognito:UserPoolId`, `Auth:Google:ClientId`, `Auth:Apple:ClientId` are all required at startup. `AddAuthModule` throws if any is missing.
- **Secret App Client**: if the Cognito App Client has a secret, `SignUpAsync` requires a `SecretHash`; computed by `SecretHashCalculator`. Prefer a secretless app client.
- **Module boundary**: only `Quadra.Modules.Auth` references the Cognito SDK and the OIDC validators. Other modules learn about new users solely through the `UserRegistered` event in `Quadra.Shared`.
- **Time source**: use injected `TimeProvider` for `OccurredAt` and `updated_at` so tests can control time deterministically.
- **Serilog**: log signup attempts with structured fields `Provider`, `OutcomeCategory` (`success | duplicate_phone | duplicate_external | oidc_invalid | cognito_policy | cognito_unavailable`). Never log raw ID tokens, the generated throwaway phone password, or the full phone number — mask the middle digits.
