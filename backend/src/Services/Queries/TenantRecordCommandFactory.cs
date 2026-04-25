using Npgsql;

namespace Api.Services;

public static class TenantRecordCommandFactory
{
    public static NpgsqlCommand CreateSelectByIdCommand(NpgsqlConnection connection, Guid tenantId)
    {
        var command = new NpgsqlCommand(TenantRecordQueryContract.BuildSelectByIdSql(), connection);
        command.Parameters.AddWithValue(TenantRecordQueryContract.TenantIdParameterName, tenantId);
        return command;
    }

    public static NpgsqlCommand CreateSelectBySlugCommand(NpgsqlConnection connection, string slug)
    {
        var command = new NpgsqlCommand(TenantRecordQueryContract.BuildSelectBySlugSql(), connection);
        command.Parameters.AddWithValue(TenantRecordQueryContract.TenantSlugParameterName, slug);
        return command;
    }
}
