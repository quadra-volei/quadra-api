using Microsoft.Extensions.Options;
using NSubstitute;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Tokens;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Shared fixtures for the Auth unit tests: a fixed clock, test JWT options, and a real
/// <see cref="SessionFactory"/> (it is pure — no I/O — so tests use it instead of a substitute).
/// </summary>
internal static class AuthTestSupport
{
    public const string SigningKey = "unit-test-signing-key-with-at-least-32-bytes!!";

    public static JwtOptions JwtOptions() => new()
    {
        Issuer = "quadra-test",
        Audience = "quadra-test-client",
        SigningKey = SigningKey,
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30,
        ClockSkewSeconds = 30,
    };

    public static IOptionsMonitor<T> Monitor<T>(T value)
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }

    public static SessionFactory SessionFactory()
    {
        var options = Monitor(JwtOptions());
        return new SessionFactory(new JwtAccessTokenIssuer(options), options);
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
