namespace Api.Services;

public static class UserLookupByEmailScalarFlow
{
    /// <summary>
    /// Read the user-id scalar result. Returns <c>null</c> when no user matched.
    /// </summary>
    public static Guid? ReadUserId(object? scalarResult)
    {
        return scalarResult is Guid userId ? userId : null;
    }
}
