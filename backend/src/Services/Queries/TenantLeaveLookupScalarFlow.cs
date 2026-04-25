namespace Api.Services;

public static class TenantLeaveLookupScalarFlow
{
    public static Guid? ReadOwnerUserId(object? scalarResult)
    {
        return scalarResult is Guid ownerUserId ? ownerUserId : null;
    }

    public static long ReadActiveAdminCount(object? scalarResult)
    {
        return scalarResult switch
        {
            long value => value,
            int value => value,
            null => 0,
            _ => Convert.ToInt64(scalarResult)
        };
    }

    public static string? ReadActiveRole(object? scalarResult)
    {
        return scalarResult as string;
    }
}
