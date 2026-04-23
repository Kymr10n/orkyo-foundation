using Npgsql;

namespace Api.Services;

/// <summary>
/// Connection-string command-timeout policy shared by Npgsql-based connection
/// factories (multi-tenant SaaS, single-tenant Community, future tooling).
///
/// Behavior: when the resulting <see cref="NpgsqlConnectionStringBuilder.CommandTimeout"/>
/// would be <c>0</c> (which Npgsql interprets as "no command timeout / infinite"),
/// the policy raises it to <see cref="DefaultCommandTimeoutSeconds"/> so a
/// stuck query cannot hold a connection indefinitely. Any non-zero value
/// already configured by the caller is preserved as-is.
///
/// Note: a connection string that does not specify <c>Command Timeout</c>
/// inherits Npgsql's own default (currently 30 s), which equals this policy's
/// default. The clamping branch only fires when the caller has explicitly set
/// <c>Command Timeout=0</c>.
/// </summary>
public static class ConnectionStringTimeoutPolicy
{
    /// <summary>
    /// Default command timeout (seconds) applied when the resulting builder
    /// reports <c>CommandTimeout == 0</c> (infinite).
    /// </summary>
    public const int DefaultCommandTimeoutSeconds = 30;

    /// <summary>
    /// Return <paramref name="connectionString"/> with the default command
    /// timeout applied if the resulting builder would have an infinite
    /// (zero) timeout. Otherwise the caller's explicit value is preserved.
    /// </summary>
    public static string ApplyDefaultCommandTimeout(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.CommandTimeout == 0)
        {
            builder.CommandTimeout = DefaultCommandTimeoutSeconds;
        }
        return builder.ConnectionString;
    }
}


