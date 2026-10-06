using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Enforces the <c>Auth:Jwt</c> settings at application startup. Failing fast here avoids booting
/// an API that signs tokens with a missing, weak, or development-only key.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private readonly IHostEnvironment _environment;

    public JwtOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Auth:Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Auth:Jwt:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add("Auth:Jwt:SigningKey is required.");
        }
        else if (Encoding.UTF8.GetByteCount(options.SigningKey) < JwtOptions.MinSigningKeyBytes)
        {
            failures.Add($"Auth:Jwt:SigningKey must be at least {JwtOptions.MinSigningKeyBytes} bytes.");
        }
        else if (options.SigningKey.StartsWith(JwtOptions.DevelopmentKeyPrefix, StringComparison.Ordinal)
            && !_environment.IsDevelopment()
            && !_environment.IsEnvironment("Testing"))
        {
            failures.Add("Auth:Jwt:SigningKey is the development key; supply a real key in this environment.");
        }

        if (options.AccessTokenMinutes <= 0)
        {
            failures.Add("Auth:Jwt:AccessTokenMinutes must be positive.");
        }

        if (options.RefreshTokenDays <= 0)
        {
            failures.Add("Auth:Jwt:RefreshTokenDays must be positive.");
        }

        if (options.ClockSkewSeconds < 0)
        {
            failures.Add("Auth:Jwt:ClockSkewSeconds must be zero or positive.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
