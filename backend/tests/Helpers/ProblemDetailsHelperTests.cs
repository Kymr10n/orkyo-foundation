using System.Text.Json;
using Api.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Helpers;

public class ProblemDetailsHelperTests
{
    // --- AuthCodes drift guards (backend ↔ frontend/contracts/errorCodes.ts AuthErrorCodes) ---

    [Fact]
    public void AuthCodes_IdentityNotLinked_ShouldMatchContract() =>
        ProblemDetailsHelper.AuthCodes.IdentityNotLinked.Should().Be("identity_not_linked");

    [Fact]
    public void AuthCodes_NotInvited_ShouldMatchContract() =>
        ProblemDetailsHelper.AuthCodes.NotInvited.Should().Be("not_invited");

    [Fact]
    public void AuthCodes_EmailNotVerified_ShouldMatchContract() =>
        ProblemDetailsHelper.AuthCodes.EmailNotVerified.Should().Be("email_not_verified");

    [Fact]
    public void AuthCodes_AccountInactive_ShouldMatchContract() =>
        ProblemDetailsHelper.AuthCodes.AccountInactive.Should().Be("account_inactive");

    [Fact]
    public void AuthCodes_InvalidToken_ShouldMatchContract() =>
        ProblemDetailsHelper.AuthCodes.InvalidToken.Should().Be("invalid_token");

    // --- AuthProblem response contract ---

    private static async Task<JsonDocument> ExecuteAndReadJson(IResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        context.RequestServices = services.BuildServiceProvider();

        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    [Fact]
    public async Task AuthProblem_ShouldDefaultTo400StatusCode()
    {
        var result = ProblemDetailsHelper.AuthProblem("identity_not_linked", "Not linked");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        context.RequestServices = services.BuildServiceProvider();

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task AuthProblem_ShouldRespectExplicitStatusCode()
    {
        var result = ProblemDetailsHelper.AuthProblem("invalid_token", "Unauthorized", statusCode: 401);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        context.RequestServices = services.BuildServiceProvider();

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task AuthProblem_ShouldEmitCodeInResponseBody()
    {
        var result = ProblemDetailsHelper.AuthProblem("not_invited", "Not invited");
        var doc = await ExecuteAndReadJson(result);

        doc.RootElement.GetProperty("code").GetString().Should().Be("not_invited");
    }

    [Fact]
    public async Task AuthProblem_ShouldEmitTitleInResponseBody()
    {
        var result = ProblemDetailsHelper.AuthProblem("account_inactive", "Account is inactive");
        var doc = await ExecuteAndReadJson(result);

        doc.RootElement.GetProperty("title").GetString().Should().Be("Account is inactive");
    }

    [Fact]
    public async Task AuthProblem_ShouldEmitTypeUriInResponseBody()
    {
        var result = ProblemDetailsHelper.AuthProblem("email_not_verified", "Email not verified");
        var doc = await ExecuteAndReadJson(result);

        doc.RootElement.GetProperty("type").GetString()
            .Should().Be("https://orkyo.app/problems/email_not_verified");
    }

    [Fact]
    public async Task AuthProblem_ShouldIncludeDetail_WhenProvided()
    {
        var result = ProblemDetailsHelper.AuthProblem("not_invited", "Not invited", detail: "Contact your admin.");
        var doc = await ExecuteAndReadJson(result);

        doc.RootElement.GetProperty("detail").GetString().Should().Be("Contact your admin.");
    }
}
