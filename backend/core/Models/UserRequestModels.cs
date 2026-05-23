namespace Api.Models;

public record ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }
    public string? NewPassword { get; init; }
    public string? ConfirmPassword { get; init; }
}

public record InviteUserRequest(string Email, UserRole Role);
public record AcceptInvitationRequest(string Token, string DisplayName, string Password);
public record UpdateUserRoleRequest(UserRole Role);
