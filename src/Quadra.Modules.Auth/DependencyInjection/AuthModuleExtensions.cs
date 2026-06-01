using Amazon;
using Amazon.CognitoIdentityProvider;
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
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Authentication;
using Quadra.Modules.Auth.Cognito;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Auth.Validation;

namespace Quadra.Modules.Auth.DependencyInjection;

/// <summary>
/// Composition-root entry points for the Auth module. Every other host (Api, future workers)
/// wires authentication exclusively through these two methods.
/// </summary>
public static class AuthModuleExtensions
{
    /// <summary>
    /// Registers Cognito-backed JWT bearer authentication and its dependencies.
    /// Fails fast at startup if the <c>Auth:Cognito</c> section is missing required values.
    /// </summary>
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidator<CognitoJwtOptions>, CognitoJwtOptionsValidator>();

        services
            .AddOptions<CognitoJwtOptions>()
            .BindConfiguration(CognitoJwtOptions.SectionName)
            .Validate(
                static (CognitoJwtOptions options, IValidator<CognitoJwtOptions> validator) =>
                {
                    var result = validator.Validate(options);
                    if (result.IsValid)
                    {
                        return true;
                    }

                    var errors = string.Join("; ", result.Errors.Select(e => e.ErrorMessage));
                    throw new OptionsValidationException(
                        nameof(CognitoJwtOptions),
                        typeof(CognitoJwtOptions),
                        [errors]);
                })
            .ValidateOnStart();

        services.AddSingleton<CognitoJwtBearerEventsHandler>();
        services.AddSingleton<IClaimsTransformation, CognitoClaimsTransformer>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptionsMonitor<CognitoJwtOptions>, CognitoJwtBearerEventsHandler>(
                static (jwt, cognitoOptionsMonitor, eventsHandler) =>
                {
                    var cognito = cognitoOptionsMonitor.CurrentValue;

                    jwt.Authority = cognito.Authority;
                    jwt.RequireHttpsMetadata = true;
                    jwt.MapInboundClaims = false;
                    jwt.Events = eventsHandler;

                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = cognito.Authority,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        // Cognito access tokens do not carry an `aud` claim — instead we validate
                        // `client_id` manually inside CognitoJwtBearerEventsHandler.OnTokenValidated.
                        ValidateAudience = false,
                        ClockSkew = TimeSpan.FromSeconds(cognito.ClockSkewSeconds),
                        NameClaimType = "cognito:username",
                        RoleClaimType = "custom:role",
                    };
                });

        services.AddAuthorization();

        AddSignupPipeline(services);

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

    private static void AddSignupPipeline(IServiceCollection services)
    {
        // Options binding + fail-fast validation for the signup pipeline.
        // We use BindConfiguration (resolved lazily through DI) so that test hosts which
        // mutate IConfiguration after AddAuthModule has been called still see their values.
        services
            .AddOptions<CognitoSignupOptions>()
            .BindConfiguration(CognitoSignupOptions.SectionName)
            .Validate(
                static options =>
                    !string.IsNullOrWhiteSpace(options.AppClientId)
                    && !string.IsNullOrWhiteSpace(options.UserPoolId)
                    && !string.IsNullOrWhiteSpace(options.Region),
                "Auth:Cognito:AppClientId, UserPoolId and Region are required.")
            .ValidateOnStart();

        services
            .AddOptions<OidcProvidersOptions>()
            .BindConfiguration(OidcProvidersOptions.SectionName)
            .Validate(
                static options =>
                    !string.IsNullOrWhiteSpace(options.Google.ClientId)
                    && !string.IsNullOrWhiteSpace(options.Apple.ClientId),
                "Auth:Google:ClientId and Auth:Apple:ClientId are required.")
            .ValidateOnStart();

        // AWS Cognito client. Region resolved from the bound options.
        services.AddSingleton<IAmazonCognitoIdentityProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CognitoSignupOptions>>().Value;
            var region = RegionEndpoint.GetBySystemName(options.Region);
            return new AmazonCognitoIdentityProviderClient(region);
        });

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

            builder.UseNpgsql(authConnection);
        });

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        services.AddSingleton<ICognitoSignupClient, CognitoSignupClient>();
        services.AddSingleton<ICognitoAuthClient, CognitoAuthClient>();
        services.AddSingleton<IOidcTokenValidator, GoogleAppleTokenValidator>();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<SignupHandler>();
        services.AddScoped<SmsOtpLoginHandler>();
        services.AddScoped<OidcLoginHandler>();
        services.AddScoped<RefreshHandler>();

        services.AddScoped<IValidator<SignupRequest>, SignupRequestValidator>();
        services.AddScoped<IValidator<SmsOtpLoginRequest>, SmsOtpLoginRequestValidator>();
        services.AddScoped<IValidator<OidcLoginRequest>, OidcLoginRequestValidator>();
        services.AddScoped<IValidator<RefreshRequest>, RefreshRequestValidator>();

        // Default IEventPublisher (no-op) — replaced by the SQS publisher when that spec lands.
        services.TryAddSingleton<IEventPublisher, NoOpEventPublisher>();
    }
}
