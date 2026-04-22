using Npgsql;

namespace Api.Services;

public static class TenantSlugAvailabilityCommandFactory
{
    public static NpgsqlCommand CreateSelectExistingTenantIdBySlugCommand(NpgsqlConnection connection, string slug)
    {
        var command = new NpgsqlCommand(TenantSlugAvailabilityQueryContract.BuildSelectExistingTenantIdBySlugSql(), connection);
        command.Parameters.AddWithValue(TenantSlugAvailabilityQueryContract.SlugParameterName, slug);
        return command;
    }
}
