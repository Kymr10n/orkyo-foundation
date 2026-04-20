using Npgsql;

namespace Api.Services;

/// <summary>
/// Foundation-level factory for creating database connections to an org's database.
/// Domain code should depend on this interface rather than the full <see cref="IDbConnectionFactory"/>.
/// </summary>
public interface IOrgDbConnectionFactory
{
    NpgsqlConnection CreateOrgConnection(OrgContext org);
}
