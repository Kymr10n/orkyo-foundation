namespace Api.Services;

public static class TenantCreationScalarFlow
{
    public static string ReadUserEmailOrEmpty(object? scalarResult)
    {
        return scalarResult as string ?? string.Empty;
    }
}
