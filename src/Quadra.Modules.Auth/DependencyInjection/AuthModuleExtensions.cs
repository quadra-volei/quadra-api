using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Quadra.Infrastructure.Messaging;
using Quadra.Infrastructure.Persistence;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Authentication;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Auth.PhoneVerification;
using Quadra.Modules.Auth.Tokens;
using Quadra.Modules.Auth.Validation;

namespace Quadra.Modules.Auth.DependencyInjection;

/// <summary>
/// Composition-root entry points for the Auth module. Every other host (Api, future workers)
/// wires authentication exclusively through these two methods.
/// </summary>
public static class AuthModuleExtensions
{
    private static readonly TimeSpan TwilioTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers JWT bearer authentication for the tokens this API issues, plus the login
    /// pipeline. Fails fast at startup if the <c>Auth</c> section is missing required values.
    /// </summary>
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // We use BindConfiguration (resolved lazily through DI) so that test hosts which
        // mutate IConfiguration after AddAuthModule has been called still see their values.
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services
            .AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<QuadraJwtBearerEventsHandler>();
        services.AddSingleton<IClaimsTransformation, QuadraClaimsTransformer>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptionsMonitor<JwtOptions>, QuadraJwtBearerEventsHandler>(
                static (jwt, jwtOptionsMonitor, eventsHandler) =>
                {
                    var options = jwtOptionsMonitor.CurrentValue;

                    jwt.MapInboundClaims = false;
                    jwt.Events = eventsHandler;

                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = options.Issuer,
                        ValidateAudience = true,
                        ValidAudience = options.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = JwtAccessTokenIssuer.CreateSigningKey(options),
                        // Pin the algorithm so a token signed any other way is never accepted.
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                        NameClaimType = QuadraClaimsTransformer.SubjectClaim,
                        RoleClaimType = QuadraClaimsTransformer.RoleClaim,
                    };
                });

        services.AddAuthorization();

        AddLoginPipeline(services);

        return services;
    }

    /// <summary>
    /// Installs the authentication + authorization middlewares in the correct order.
    /// Must be called between <c>UseRouting</c> and the endpoint mappings.
    /// </summary>
    public static IApplicationBuilder UseAuthModule(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    private static void AddLoginPipeline(IServiceCollection services)
    {
        // Apple login is not enabled yet, so only the Google client ID is mandatory.
        services
            .AddOptions<OidcProvidersOptions>()
            .BindConfiguration(OidcProvidersOptions.SectionName)
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.Google.ClientId),
                "Auth:Google:ClientId is required.")
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<PhoneVerificationOptions>, PhoneVerificationOptionsValidator>();
        services
            .AddOptions<PhoneVerificationOptions>()
            .BindConfiguration(PhoneVerificationOptions.SectionName)
            .ValidateOnStart();

        services
            .AddHttpClient(TwilioVerifyPhoneVerificationService.HttpClientName)
            .ConfigureHttpClient(static client => client.Timeout = TwilioTimeout);

        services.AddSingleton<FakePhoneVerificationService>();
        services.AddSingleton<TwilioVerifyPhoneVerificationService>();
        services.AddSingleton<IPhoneVerificationService>(static sp =>
            sp.GetRequiredService<IOptions<PhoneVerificationOptions>>().Value.UsesFake
                ? sp.GetRequiredService<FakePhoneVerificationService>()
                : sp.GetRequiredService<TwilioVerifyPhoneVerificationService>());

        // EF persistence — connection string comes from the standard ConnectionStrings:Auth slot,
        // falling back to ConnectionStrings:Default so tests with a single Postgres can share it.
        // Resolved lazily via IServiceProvider so the test host's configuration overrides
        // (applied through ConfigureAppConfiguration after AddAuthModule runs) are honoured.
        services.AddDbContext<AuthDbContext>((sp, builder) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var authConnection = cfg.GetConnectionString("Auth")
                ?? cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Auth (or ConnectionStrings:Default) must be configured.");

            builder.UseNpgsql(PostgresConnectionString.Normalize(authConnection));
        });

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        services.AddSingleton<IOidcTokenValidator, GoogleAppleTokenValidator>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<SessionFactory>();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<UserProvisioner>();
        services.AddScoped<SmsOtpLoginHandler>();
        services.AddScoped<OidcLoginHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<LogoutHandler>();

        services.AddScoped<IValidator<SmsOtpLoginRequest>, SmsOtpLoginRequestValidator>();
        services.AddScoped<IValidator<OidcLoginRequest>, OidcLoginRequestValidator>();
        services.AddScoped<IValidator<RefreshRequest>, RefreshRequestValidator>();
        services.AddScoped<IValidator<LogoutRequest>, LogoutRequestValidator>();

        // Default IEventPublisher (no-op) — replaced by the SQS publisher when that spec lands.
        services.TryAddSingleton<IEventPublisher, NoOpEventPublisher>();
    }
}
