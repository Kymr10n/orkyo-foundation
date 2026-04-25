namespace Api.Services;

public static class TenantSlugAvailabilityScalarFlow
{
    public static bool IsTaken(object? scalarResult)
    {
        return scalarResult != null && scalarResult != DBNull.Value;
    }
}
