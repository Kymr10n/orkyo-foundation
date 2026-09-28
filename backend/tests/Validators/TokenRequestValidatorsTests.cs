using Api.Constants;
using Api.Endpoints.PlatformApi;
using Api.Endpoints.Reporting;
using Api.Security;
using Api.Validators;

namespace Orkyo.Foundation.Tests.Validators;

/// <summary>The two token kinds share one name cap: both name columns are VARCHAR(255).</summary>
public class TokenRequestValidatorsTests
{
    [Fact]
    public void BothTokenKinds_AcceptAndCapANameAtTheColumnWidth()
    {
        var reporting = new CreateReportingTokenRequestValidator(TimeProvider.System);
        var api = new CreateApiAccessTokenRequestValidator(TimeProvider.System);
        var atCap = new string('n', DomainLimits.TokenNameMaxLength);
        var overCap = atCap + "n";

        // The reporting validator used to stop at 200.
        reporting.Validate(new CreateReportingTokenRequest(atCap, null)).IsValid.Should().BeTrue();
        reporting.Validate(new CreateReportingTokenRequest(overCap, null)).IsValid.Should().BeFalse();
        api.Validate(new CreateApiAccessTokenRequest(atCap, [PlatformApiScopes.ScheduleRead], null)).IsValid.Should().BeTrue();
        api.Validate(new CreateApiAccessTokenRequest(overCap, [PlatformApiScopes.ScheduleRead], null)).IsValid.Should().BeFalse();
    }
}
