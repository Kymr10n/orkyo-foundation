using Api.Models;
using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantContextTests
{
    private static TenantContext Make(string status, string? suspensionReason = null) => new()
    {
        TenantId = Guid.NewGuid(),
        TenantSlug = "test-tenant",
        TenantDbConnectionString = "Host=localhost;Database=test;",
        Tier = ServiceTier.Free,
        Status = status,
        SuspensionReason = suspensionReason
    };

    [Theory]
    [InlineData(TenantStatusConstants.Active, true, false)]
    [InlineData(TenantStatusConstants.Suspended, false, true)]
    [InlineData(TenantStatusConstants.Deleting, false, false)]
    [InlineData(TenantStatusConstants.Pending, false, false)]
    public void StatusFlags_ShouldReflectStatusValue(string status, bool isActive, bool isSuspended)
    {
        var ctx = Make(status);

        ctx.IsActive.Should().Be(isActive);
        ctx.IsSuspended.Should().Be(isSuspended);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    [InlineData("Active")]
    public void IsActive_ShouldBeCaseInsensitive(string status)
    {
        var ctx = Make(status);

        ctx.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("SUSPENDED")]
    [InlineData("Suspended")]
    public void IsSuspended_ShouldBeCaseInsensitive(string status)
    {
        var ctx = Make(status);

        ctx.IsSuspended.Should().BeTrue();
    }

    [Fact]
    public void SuspensionReason_ShouldBeNull_WhenNotSuspended()
    {
        var ctx = Make(TenantStatusConstants.Active);

        ctx.SuspensionReason.Should().BeNull();
    }

    [Fact]
    public void SuspensionReason_ShouldBePreserved_WhenSuspended()
    {
        var ctx = Make(TenantStatusConstants.Suspended, SuspensionReasonConstants.Inactivity);

        ctx.IsSuspended.Should().BeTrue();
        ctx.SuspensionReason.Should().Be(SuspensionReasonConstants.Inactivity);
    }
}
