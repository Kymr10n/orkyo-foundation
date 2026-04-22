using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantStatusPolicyTests
{
    [Theory]
    [InlineData(TenantStatusConstants.Active, true)]
    [InlineData("ACTIVE", true)]
    [InlineData(TenantStatusConstants.Suspended, false)]
    [InlineData(TenantStatusConstants.Pending, false)]
    public void IsActive_ShouldInterpretStatus(string status, bool expected)
    {
        var result = TenantStatusPolicy.IsActive(status);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(TenantStatusConstants.Suspended, true)]
    [InlineData("SUSPENDED", true)]
    [InlineData(TenantStatusConstants.Active, false)]
    [InlineData(TenantStatusConstants.Deleting, false)]
    public void IsSuspended_ShouldInterpretStatus(string status, bool expected)
    {
        var result = TenantStatusPolicy.IsSuspended(status);

        result.Should().Be(expected);
    }
}