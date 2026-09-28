using Api.Endpoints;
using Api.Models;
using Api.Validators;

namespace Orkyo.Foundation.Tests.Validators;

/// <summary>
/// Shape rules of the contact, feedback, account-creation and email-change forms. The endpoint
/// suites keep one HTTP 400 each to prove the validator is wired; the rules live here.
/// </summary>
public class FormRequestValidatorsTests
{
    private static ContactRequest Contact(
        string? name = "Test", string? email = "valid@test.local", string? subject = "demo",
        string? message = "Hello", string? company = null, string? challengeToken = null) => new()
        {
            Name = name!,
            Email = email!,
            Subject = subject!,
            Message = message!,
            Company = company,
            ChallengeToken = challengeToken,
        };

    public static TheoryData<string, ContactRequest> InvalidContacts => new()
    {
        { "null name", Contact(name: null) },
        { "empty name", Contact(name: "") },
        { "blank name", Contact(name: "   ") },
        { "name over 200", Contact(name: new string('a', 201)) },
        { "null email", Contact(email: null) },
        { "empty email", Contact(email: "") },
        { "malformed email", Contact(email: "not-an-email") },
        { "null subject", Contact(subject: null) },
        { "empty subject", Contact(subject: "") },
        { "unknown subject", Contact(subject: "invalid-subject") },
        { "subject in the wrong case", Contact(subject: "DEMO") },
        { "null message", Contact(message: null) },
        { "empty message", Contact(message: "") },
        { "blank message", Contact(message: "   ") },
        { "message over 5000", Contact(message: new string('x', 5001)) },
        { "company over 200", Contact(company: new string('c', 201)) },
        { "challenge token over 2048", Contact(challengeToken: new string('t', 2049)) },
    };

    [Theory]
    [MemberData(nameof(InvalidContacts))]
    public void Contact_Invalid_Fails(string because, ContactRequest request) =>
        Assert.False(new ContactRequestValidator().Validate(request).IsValid, because);

    [Fact]
    public void Contact_Valid_Passes() =>
        Assert.True(new ContactRequestValidator().Validate(Contact(company: "Acme")).IsValid);

    [Theory]
    [InlineData("invalid_type", "Some feedback")]
    [InlineData("bug", "")]
    [InlineData("bug", null)]
    public void Feedback_Invalid_Fails(string type, string? title) =>
        Assert.False(new CreateFeedbackRequestValidator()
            .Validate(new CreateFeedbackRequest { FeedbackType = type, Title = title! }).IsValid);

    [Fact]
    public void Feedback_TitleOverTheLimit_Fails() =>
        Assert.False(new CreateFeedbackRequestValidator().Validate(new CreateFeedbackRequest
        {
            FeedbackType = "bug",
            Title = new string('A', Api.Constants.DomainLimits.FeedbackTitleMaxLength + 1),
        }).IsValid);

    [Theory]
    [InlineData("", "SecurePass123!", "Email is required")]
    [InlineData("not-an-email", "SecurePass123!", "Invalid email format")]
    [InlineData("test@example.com", "", "Password is required")]
    [InlineData("test@example.com", "short", "Password must be at least")]
    public void CreateAccount_Invalid_FailsWithItsMessage(string email, string password, string message)
    {
        var result = new CreateAccountRequestValidator()
            .Validate(new CreateAccountRequest { Email = email, Password = password });

        Assert.Contains(result.Errors, e => e.ErrorMessage.StartsWith(message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void EmailChange_InvalidAddress_Fails(string newEmail) =>
        Assert.False(new RequestEmailChangeRequestValidator().Validate(new RequestEmailChangeRequest(newEmail)).IsValid);
}
