using Npgsql;

namespace Api.Services;

public static class TenantOwnerStatusCommandFactory
{
    public static NpgsqlCommand CreateSelectByTenantIdCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantOwnerStatusQueryContract.BuildSelectByTenantIdSql(), connection);
        command.Parameters.AddWithValue(TenantOwnerStatusQueryContract.TenantIdParameterName, tenantId);
        return command;
    }
}
