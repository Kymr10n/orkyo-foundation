namespace Api.Models;

public record RemoveMfaRequest
{
    public string? CurrentPassword { get; init; }

    /// <summary>The current TOTP code: the password grant that proves the password rejects a TOTP user without it.</summary>
    public string? CurrentCode { get; init; }
}

public record RemovePasskeyRequest
{
    public string? CurrentPassword { get; init; }

    /// <summary>Required only when the user has TOTP: the password grant refuses such a user without it.</summary>
    public string? CurrentCode { get; init; }
}

public record RenamePasskeyRequest
{
    public string? Label { get; init; }
}

public record ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }
    public string? NewPassword { get; init; }
    public string? ConfirmPassword { get; init; }
}

public record InviteUserRequest(string Email, UserRole Role);
public record AcceptInvitationRequest(string Token, string DisplayName, string Password);
public record UpdateUserRoleRequest(UserRole Role);
