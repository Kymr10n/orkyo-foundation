using System.Data.Common;
using Npgsql;

namespace Api.Services;

public static class TenantResolverDbFlow
{
    public static async Task<TenantContext?> ResolveFromOpenConnectionAsync(
        NpgsqlConnection connection,
        string tenantSlug,
        string controlPlaneConnectionString,
        Func<NpgsqlConnection, string, NpgsqlCommand>? commandFactory = null,
        Func<NpgsqlCommand, Task<DbDataReader>>? readerExecutor = null)
    {
        commandFactory ??= TenantResolverCommandFactory.CreateSelectBySlugCommand;
        if (readerExecutor == null)
        {
            readerExecutor = static async cmd => await cmd.ExecuteReaderAsync();
        }

        await using var command = commandFactory(connection, tenantSlug);
        await using var reader = await readerExecutor(command);
        return await TenantResolverReaderFlow.ReadSingleOrNullAsync(reader, controlPlaneConnectionString);
    }
}