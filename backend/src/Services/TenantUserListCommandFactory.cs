using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Api.Services;

public static class TenantUserListCommandFactory
{
    /// <summary>
    /// Build the tenant user-listing command bound to the given tenant id.
    /// </summary>
    public static NpgsqlCommand CreateListUsersByTenantCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantUserListQueryContract.BuildListUsersByTenantSql(), connection);
        command.Parameters.AddWithValue(TenantUserListQueryContract.TenantIdParameterName, tenantId);
        return command;
    }
}

public static class TenantUserListReaderFlow
{
    /// <summary>
    /// Drain an open <see cref="NpgsqlDataReader"/> into a list of
    /// <see cref="User"/> projections via <see cref="UserHelper.MapUser"/>.
    /// </summary>
    public static async Task<List<User>> ReadUsersAsync(NpgsqlDataReader reader)
    {
        var users = new List<User>();
        while (await reader.ReadAsync())
        {
            users.Add(UserHelper.MapUser(reader));
        }
        return users;
    }
}
