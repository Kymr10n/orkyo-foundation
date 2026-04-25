namespace Api.Security;

public class AuthorizationException : Exception
{
    public int StatusCode { get; }

    public AuthorizationException(string message, int statusCode = 403) : base(message)
    {
        StatusCode = statusCode;
    }

    public static AuthorizationException NotAuthenticated() =>
        new("Authentication required", 401);

    public static AuthorizationException NotMember() =>
        new("You are not a member of this tenant");

    public static AuthorizationException InsufficientRole(TenantRole required, TenantRole actual) =>
        new($"Role '{required}' required, but you have '{actual}'");

    public static AuthorizationException TenantNotFound() =>
        new("Tenant not found", 404);
}
