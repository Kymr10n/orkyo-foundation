using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Helpers;

/// <summary>
/// Response-contract tests for the canonical problem builder, plus drift guards pinning
/// the auth error codes to the frontend contract. Successor to the
/// <c>ProblemDetailsHelper</c> tests — the helper was a pure alias layer over
/// <see cref="ProblemResults"/> / <see cref="ApiErrorCodes.Auth"/> and was inlined.
/// </summary>
public class ProblemResultsTests
{
    // --- Auth code drift guards (backend ↔ frontend/contracts/errorCodes.ts AuthErrorCodes) ---

    [Fact]
    public void AuthCodes_IdentityNotLinked_ShouldMatchContract() =>
        ApiErrorCodes.Auth.IdentityNotLinked.Should().Be("identity_not_linked");

    [Fact]
    public void AuthCodes_NotInvited_ShouldMatchContract() =>
        ApiErrorCodes.Auth.NotInvited.Should().Be("not_invited");

    [Fact]
    public void AuthCodes_EmailNotVerified_ShouldMatchContract() =>
        ApiErrorCodes.Auth.EmailNotVerified.Should().Be("email_not_verified");

    [Fact]
    public void AuthCodes_AccountInactive_ShouldMatchContract() =>
        ApiErrorCodes.Auth.AccountInactive.Should().Be("account_inactive");

    [Fact]
    public void AuthCodes_InvalidToken_ShouldMatchContract() =>
        ApiErrorCodes.Auth.InvalidToken.Should().Be("invalid_token");

    // --- Problem response contract ---

    private static async Task<(int StatusCode, JsonDocument Body)> ExecuteAndReadJson(IResult result)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        context.RequestServices = services.BuildServiceProvider();

        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await JsonDocument.ParseAsync(context.Response.Body));
    }

    [Fact]
    public async Task Problem_ShouldEmitTheRequestedStatusCode()
    {
        var (status, _) = await ExecuteAndReadJson(
            ProblemResults.Problem(401, ApiErrorCodes.Auth.InvalidToken, title: "Unauthorized"));

        status.Should().Be(401);
    }

    [Fact]
    public async Task Problem_ShouldEmitCodeInResponseBody()
    {
        var (_, doc) = await ExecuteAndReadJson(
            ProblemResults.Problem(403, ApiErrorCodes.Auth.NotInvited, title: "Not invited"));

        doc.RootElement.GetProperty("code").GetString().Should().Be("not_invited");
    }

    [Fact]
    public async Task Problem_ShouldEmitTitleInResponseBody()
    {
        var (_, doc) = await ExecuteAndReadJson(
            ProblemResults.Problem(400, ApiErrorCodes.Auth.AccountInactive, title: "Account is inactive"));

        doc.RootElement.GetProperty("title").GetString().Should().Be("Account is inactive");
    }

    [Fact]
    public async Task Problem_ShouldEmitTypeUriInResponseBody()
    {
        var (_, doc) = await ExecuteAndReadJson(
            ProblemResults.Problem(400, ApiErrorCodes.Auth.EmailNotVerified, title: "Email not verified"));

        doc.RootElement.GetProperty("type").GetString()
            .Should().Be("https://orkyo.app/problems/email_not_verified");
    }

    [Fact]
    public async Task Problem_ShouldIncludeDetail_WhenProvided()
    {
        var (_, doc) = await ExecuteAndReadJson(
            ProblemResults.Problem(403, ApiErrorCodes.Auth.NotInvited, detail: "Contact your admin.", title: "Not invited"));

        doc.RootElement.GetProperty("detail").GetString().Should().Be("Contact your admin.");
    }
}
