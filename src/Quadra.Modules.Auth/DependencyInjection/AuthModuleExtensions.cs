using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Quadra.Modules.Auth.Authentication;
using Quadra.Modules.Auth.Configuration;

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
            .Bind(configuration.GetSection(CognitoJwtOptions.SectionName))
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
                        new[] { errors });
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
}
