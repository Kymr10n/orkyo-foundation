namespace Api.Security;

public class AuthorizationException : Exception
{
    public int StatusCode { get; }

    public AuthorizationException(string message, int statusCode = 403) : base(message)
    {
        StatusCode = statusCode;
    }

    public static AuthorizationException NotMember() =>
        new("You are not a member of this tenant");

    public static AuthorizationException TenantNotFound() =>
        new("Tenant not found", 404);
}
