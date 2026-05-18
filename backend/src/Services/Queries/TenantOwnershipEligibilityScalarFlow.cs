namespace Api.Services;

public static class TenantOwnershipEligibilityScalarFlow
{
    public static bool CanCreateTenant(object? scalarResult)
    {
        var count = scalarResult switch
        {
            long value => value,
            int value => value,
            null => 0,
            _ => Convert.ToInt64(scalarResult)
        };

        return count == 0;
    }
}
