using Api.Endpoints;
using Api.Models;
using Api.Validators;
using FluentValidation;

namespace Orkyo.Foundation.Tests.Validators;

public class UserValidatorTests
{
    private readonly IValidator<InviteUserRequest> _inviteValidator = new InviteUserRequestValidator();
    private readonly IValidator<AcceptInvitationRequest> _acceptValidator = new AcceptInvitationRequestValidator();
    private readonly IValidator<UpdateUserRoleRequest> _roleValidator = new UpdateUserRoleRequestValidator();

    #region Profile and email change

    [Fact]
    public void Profile_ANameOverTheDisplayNameColumn_Fails()
    {
        // Keycloak is updated before users.display_name; an overlong name changed the one and 500ed on the other.
        var validator = new UpdateProfileRequestValidator();
        var tooLong = new string('a', Api.Constants.DomainLimits.PersonNamePartMaxLength + 1);

        Assert.False(validator.Validate(new UpdateProfileRequest { FirstName = tooLong }).IsValid);
        Assert.False(validator.Validate(new UpdateProfileRequest { FirstName = "Ann", LastName = tooLong }).IsValid);
        Assert.True(validator.Validate(new UpdateProfileRequest { FirstName = "Ann", LastName = "Lee" }).IsValid);
    }

    [Fact]
    public void EmailChange_AnAddressOverTheColumn_Fails()
    {
        var validator = new RequestEmailChangeRequestValidator();
        var local = new string('a', 64);
        var domain = string.Join('.', Enumerable.Repeat(new string('b', 60), 5)) + ".com";

        Assert.False(validator.Validate(new RequestEmailChangeRequest($"{local}@{domain}")).IsValid);
    }

    [Fact]
    public void RemoveMfa_RequiresThePasswordAndASixDigitCode()
    {
        var validator = new RemoveMfaRequestValidator();

        Assert.True(validator.Validate(new RemoveMfaRequest { CurrentPassword = "pw", CurrentCode = "123456" }).IsValid);
        Assert.False(validator.Validate(new RemoveMfaRequest { CurrentPassword = "pw" }).IsValid);
        Assert.False(validator.Validate(new RemoveMfaRequest { CurrentPassword = "pw", CurrentCode = "12345" }).IsValid);
        Assert.False(validator.Validate(new RemoveMfaRequest { CurrentPassword = "pw", CurrentCode = "1234567" }).IsValid);
        Assert.False(validator.Validate(new RemoveMfaRequest { CurrentPassword = "pw", CurrentCode = "12345a" }).IsValid);
        Assert.False(validator.Validate(new RemoveMfaRequest { CurrentCode = "123456" }).IsValid);
    }

    #endregion

    #region InviteUserRequest

    [Fact]
    public void Invite_ValidRequest_Passes()
    {
        var result = _inviteValidator.Validate(new InviteUserRequest("user@example.com", UserRole.Editor));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Editor)]
    [InlineData(UserRole.Viewer)]
    public void Invite_AllValidRoles_Pass(UserRole role)
    {
        var result = _inviteValidator.Validate(new InviteUserRequest("user@example.com", role));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Invite_EmptyEmail_Fails(string? email)
    {
        var result = _inviteValidator.Validate(new InviteUserRequest(email!, UserRole.Editor));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Email");
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("@missing-local")]
    [InlineData("missing-domain@")]
    public void Invite_InvalidEmail_Fails(string email)
    {
        var result = _inviteValidator.Validate(new InviteUserRequest(email, UserRole.Editor));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("valid email"));
    }

    [Fact]
    public void Invite_InvalidRole_Fails()
    {
        var result = _inviteValidator.Validate(new InviteUserRequest("user@example.com", (UserRole)99));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Role is not valid"));
    }

    #endregion

    #region AcceptInvitationRequest

    [Fact]
    public void Accept_ValidRequest_Passes()
    {
        var result = _acceptValidator.Validate(new AcceptInvitationRequest("abc123token", "John Doe", "SecurePass1"));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Accept_EmptyToken_Fails(string? token)
    {
        var result = _acceptValidator.Validate(new AcceptInvitationRequest(token!, "John", "SecurePass1"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Token is required"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Accept_EmptyDisplayName_Fails(string? displayName)
    {
        var result = _acceptValidator.Validate(new AcceptInvitationRequest("token", displayName!, "SecurePass1"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Display name is required"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Accept_EmptyPassword_Fails(string? password)
    {
        var result = _acceptValidator.Validate(new AcceptInvitationRequest("token", "John", password!));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Password is required"));
    }

    [Fact]
    public void Accept_PasswordTooShort_Fails()
    {
        var result = _acceptValidator.Validate(new AcceptInvitationRequest("token", "John", "short"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least 8 characters"));
    }

    [Fact]
    public void Accept_ExactlyMinPasswordLength_Passes()
    {
        var result = _acceptValidator.Validate(new AcceptInvitationRequest("token", "John", "12345678"));
        Assert.True(result.IsValid);
    }

    #endregion

    #region UpdateUserRoleRequest

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Editor)]
    [InlineData(UserRole.Viewer)]
    public void UpdateRole_ValidRoles_Pass(UserRole role)
    {
        var result = _roleValidator.Validate(new UpdateUserRoleRequest(role));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateRole_InvalidRole_Fails()
    {
        var result = _roleValidator.Validate(new UpdateUserRoleRequest((UserRole)99));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Role is not valid"));
    }

    #endregion
}
