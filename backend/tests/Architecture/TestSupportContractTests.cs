using System.Net;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// The shared architecture helpers in <c>Orkyo.Foundation.TestSupport</c> are consumed by the
/// products' <c>ExplicitRegistrationTests</c> / <c>RouteInventoryTests</c>; these pin the
/// classification and probe semantics where the code lives.
/// </summary>
public class TestSupportContractTests
{
    [Fact]
    public void ExplicitRegistration_ReportsMissingAdd_AndUnclassifiedUse()
    {
        const string program = """
            builder.Services.AddAuthorization();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRouting();
            app.UseMystery();
            """;

        var findings = ExplicitRegistrationContract.Check(program);

        findings.Uses.Should().Equal("UseAuthentication", "UseAuthorization", "UseMystery", "UseRouting");
        findings.Unclassified.Should().Equal("UseMystery");
        findings.Missing.Should().ContainSingle().Which.Should().StartWith("UseAuthentication (expected one of:");
        findings.ExplainUnclassified().Should().Contain("UseMystery");
        findings.ExplainMissing().Should().Contain("UseAuthentication");
    }

    [Fact]
    public void ExplicitRegistration_EditionRowsExtendAndOverrideTheSharedMap()
    {
        const string program = """
            builder.Services.AddEditionThing();
            builder.Services.AddOtherAuth();
            app.UseEditionThing();
            app.UseAuthorization();
            app.UseLegacy();
            app.UseNothing();
            """;

        var findings = ExplicitRegistrationContract.Check(
            program,
            extraUseToAdd: new Dictionary<string, string[]>
            {
                ["UseEditionThing"] = ["AddEditionThing"],
                ["UseAuthorization"] = ["AddOtherAuth"],
            },
            extraNoRegistrationNeeded: ["UseNothing"],
            knownTransitionalExceptions: ["UseLegacy"]);

        findings.Unclassified.Should().BeEmpty();
        findings.Missing.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitRegistration_FindProgramCs_ReturnsNullOutsideACheckout()
    {
        ExplicitRegistrationContract.FindProgramCs(Path.GetTempPath()).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task RouteInventoryProbe_FlagsOnlyUnmappedOrBrokenRoutes(HttpStatusCode status, bool expectFailure)
    {
        using var client = new HttpClient(new FixedStatusHandler(status)) { BaseAddress = new Uri("http://localhost") };

        var failure = await RouteInventoryProbe.ProbeAsync(client, "/api/sites", "MapSiteEndpoints");

        if (expectFailure)
            failure.Should().Contain("/api/sites").And.Contain("MapSiteEndpoints").And.Contain(((int)status).ToString());
        else
            failure.Should().BeNull();
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }
}
