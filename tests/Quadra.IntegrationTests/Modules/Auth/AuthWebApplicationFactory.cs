using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// Spins up the real Quadra.Api host in-memory and replaces the JWT bearer's
/// OIDC discovery with a static configuration that trusts <see cref="TestSigningKeys.PublicKey"/>.
/// Lets tests issue tokens locally without contacting Cognito.
/// </summary>
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestUserPoolId = "us-east-1_TESTPOOL1";
    public const string TestRegion = "us-east-1";
    public const string TestAudience = "test-client-id";
    public static string TestIssuer => $"https://cognito-idp.{TestRegion}.amazonaws.com/{TestUserPoolId}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Cognito:UserPoolId"] = TestUserPoolId,
                ["Auth:Cognito:Region"] = TestRegion,
                ["Auth:Cognito:Audience"] = TestAudience,
                ["Auth:Cognito:ClockSkewSeconds"] = "30",
                // FA.2 added a signup pipeline that fails fast on missing options. These tests
                // only exercise the FA.1 JWT middleware, but the host still wires the signup
                // pipeline, so we supply stub values to satisfy ValidateOnStart.
                ["Auth:Cognito:AppClientId"] = TestAudience,
                ["Auth:Google:ClientId"] = "test-google-client",
                ["Auth:Google:Issuer"] = "https://accounts.google.com",
                ["Auth:Google:JwksUri"] = "https://accounts.google.com/.well-known/openid-configuration",
                ["Auth:Apple:ClientId"] = "test-apple-client",
                ["Auth:Apple:Issuer"] = "https://appleid.apple.com",
                ["Auth:Apple:JwksUri"] = "https://appleid.apple.com/.well-known/openid-configuration",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Register the test controllers (TestProtectedController, TestHubProtectedController, …).
            services
                .AddControllers()
                .AddApplicationPart(typeof(TestProtectedController).Assembly);

            // PostConfigure runs AFTER the module's Configure, letting us replace OIDC discovery
            // with an in-process static signing-key configuration so the middleware never hits
            // the network. Authority/MetadataAddress are cleared and a StaticConfigurationManager
            // is supplied with TestSigningKeys.PublicKey.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
            {
                jwt.Authority = null;
                jwt.MetadataAddress = null!;
                jwt.RequireHttpsMetadata = false;

                var staticConfig = new OpenIdConnectConfiguration
                {
                    Issuer = TestIssuer,
                };
                staticConfig.SigningKeys.Add(TestSigningKeys.PublicKey);

                jwt.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(staticConfig);

                jwt.TokenValidationParameters.ValidIssuer = TestIssuer;
                jwt.TokenValidationParameters.IssuerSigningKeys = new[] { TestSigningKeys.PublicKey };
                jwt.TokenValidationParameters.ValidateIssuerSigningKey = true;
                jwt.TokenValidationParameters.ValidateIssuer = true;
                jwt.TokenValidationParameters.ValidateLifetime = true;
                jwt.TokenValidationParameters.ValidateAudience = false;
            });
        });
    }
}
