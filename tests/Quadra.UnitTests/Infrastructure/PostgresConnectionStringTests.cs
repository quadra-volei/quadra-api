using FluentAssertions;
using Npgsql;
using Quadra.Infrastructure.Persistence;

namespace Quadra.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="PostgresConnectionString"/>: hosted databases hand out
/// <c>postgresql://</c> URLs, which must become a connection string Npgsql accepts.
/// </summary>
public sealed class PostgresConnectionStringTests
{
    /// <summary>
    /// Covers: a key=value connection string (local development) is passed through untouched.
    /// </summary>
    [Fact]
    public void Key_value_connection_string_is_returned_unchanged()
    {
        const string keyValue = "Host=localhost;Port=5432;Database=quadra;Username=quadra;Password=quadra";

        PostgresConnectionString.Normalize(keyValue).Should().Be(keyValue);
    }

    /// <summary>
    /// Covers: a Neon-style URL (no port, TLS options, URL-encoded password) is converted.
    /// </summary>
    [Theory]
    [InlineData("postgresql://")]
    [InlineData("postgres://")]
    public void Url_is_converted_to_an_npgsql_connection_string(string scheme)
    {
        var url = scheme + "app_user:p%40ss%3Aword%2F1@ep-cool-name.us-east-1.aws.neon.tech/quadra_db"
            + "?sslmode=require&channel_binding=require";

        var parsed = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(url));

        parsed.Host.Should().Be("ep-cool-name.us-east-1.aws.neon.tech");
        parsed.Port.Should().Be(5432);
        parsed.Database.Should().Be("quadra_db");
        parsed.Username.Should().Be("app_user");
        parsed.Password.Should().Be("p@ss:word/1");
        parsed.SslMode.Should().Be(SslMode.Require);
        parsed.ChannelBinding.Should().Be(ChannelBinding.Require);
    }

    /// <summary>
    /// Covers: an explicit port is kept and unknown query parameters are ignored.
    /// </summary>
    [Fact]
    public void Url_with_port_and_unknown_parameters_is_converted()
    {
        const string url = "postgres://u:p@db.internal:6543/app?sslmode=verify-full&options=endpoint%3Dabc";

        var parsed = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(url));

        parsed.Host.Should().Be("db.internal");
        parsed.Port.Should().Be(6543);
        parsed.Database.Should().Be("app");
        parsed.SslMode.Should().Be(SslMode.VerifyFull);
    }

    /// <summary>
    /// Covers: a malformed URL fails without leaking the secret into the error message.
    /// </summary>
    [Fact]
    public void Malformed_url_throws_without_echoing_the_value()
    {
        var act = () => PostgresConnectionString.Normalize("postgresql://user:topsecret@:bad");

        act.Should().Throw<FormatException>().Which.Message.Should().NotContain("topsecret");
    }
}
