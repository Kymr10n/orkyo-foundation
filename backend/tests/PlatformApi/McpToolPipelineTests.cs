using Api.Helpers;
using Api.PlatformApi.Mcp;

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
}
