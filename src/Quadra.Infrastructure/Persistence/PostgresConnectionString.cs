using Npgsql;

namespace Quadra.Infrastructure.Persistence;

/// <summary>
/// Accepts a PostgreSQL connection string in either form and returns the Npgsql key=value form.
/// Hosted databases (Neon, Render, Fly) hand out <c>postgresql://user:pass@host:port/db?sslmode=…</c>
/// URLs, which Npgsql does not parse; a key=value string is returned unchanged.
/// </summary>
public static class PostgresConnectionString
{
    private const int DefaultPort = 5432;

    public static string Normalize(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var trimmed = connectionString.Trim();
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        // Never echo the value in the exception: it contains the password.
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new FormatException("The PostgreSQL connection URL is malformed.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? DefaultPort : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo[0].Length > 0)
        {
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        }

        if (userInfo.Length == 2)
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

            // Only the TLS options are carried over; other libpq-only parameters (e.g. options=…)
            // have no Npgsql equivalent and would make the connection string invalid.
            switch (parts[0].ToLowerInvariant())
            {
                case "sslmode" when Enum.TryParse<SslMode>(value.Replace("-", string.Empty), ignoreCase: true, out var sslMode):
                    builder.SslMode = sslMode;
                    break;
                case "channel_binding" when Enum.TryParse<ChannelBinding>(value, ignoreCase: true, out var channelBinding):
                    builder.ChannelBinding = channelBinding;
                    break;
            }
        }

        return builder.ConnectionString;
    }
}
