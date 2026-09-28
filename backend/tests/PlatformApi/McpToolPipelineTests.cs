using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.PlatformApi.Mcp;
using Microsoft.AspNetCore.Http;

namespace Orkyo.Foundation.Tests.PlatformApi;

/// <summary>
/// Which exceptions the MCP pipeline hands to the agent with their own message: the same set
/// AppExceptionHandler answers with a 4xx. Everything else stays a generic failure.
/// </summary>
public class McpToolPipelineTests
{
    public static TheoryData<Exception> Refusals() => new()
    {
        new NotFoundException("Request", Guid.NewGuid()),
        new ConflictException("This dependency would create a circular reference"),
        new ArgumentException("Unknown or inactive resource type(s): lathe."),
        new FeatureNotAvailableException("auto_schedule", "not on this plan"),
        new QuotaExceededException("requests", 10),
        new CapabilityNotApplicableException(Guid.NewGuid(), Guid.NewGuid(), "not applicable"),
        // The two the pipeline's own list had drifted from: HTTP answered them with a 4xx already.
        new AccountLockedException("This account is locked."),
        new KeycloakAdminException("User not found in Keycloak.", StatusCodes.Status404NotFound),
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void ADomainException_ReachesTheAgentWithItsMessage(Exception ex)
        => McpToolPipeline.DomainRefusal(ex).Should().Be(ex.Message);

    [Fact]
    public void AGuardClauseFailure_StaysGeneric()
    {
        // Their messages name internal parameters; HTTP answers them with a bare 500 as well.
        McpToolPipeline.DomainRefusal(new ArgumentNullException("orgContext")).Should().BeNull();
        McpToolPipeline.DomainRefusal(new ArgumentOutOfRangeException("limit")).Should().BeNull();
    }

    [Fact]
    public void AnInfrastructureException_StaysGeneric()
        => McpToolPipeline.DomainRefusal(new InvalidOperationException("SELECT * FROM requests")).Should().BeNull();

    [Fact]
    public void AnUnauthorizedAccess_ReachesTheAgentAsTheHttpForbidden_NotItsOwnMessage()
        // The 403 detail is what HTTP answers; the exception's own text can name a path.
        => McpToolPipeline.DomainRefusal(new UnauthorizedAccessException("/var/lib/orkyo")).Should().Be("Forbidden");

    [Fact]
    public void AnUpstreamKeycloakFailure_StaysGeneric()
        // A 5xx from Keycloak is not the agent's to act on; HTTP answers it with a 502 as well.
        => McpToolPipeline.DomainRefusal(new KeycloakAdminException("Keycloak is unavailable.")).Should().BeNull();
}
