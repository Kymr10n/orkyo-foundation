using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantCreationScalarFlowTests
{
    [Fact]
    public void ReadUserEmailOrEmpty_ShouldReturnString_WhenScalarIsString()
    {
        TenantCreationScalarFlow.ReadUserEmailOrEmpty("owner@example.com").Should().Be("owner@example.com");
    }

    [Fact]
    public void ReadUserEmailOrEmpty_ShouldReturnEmpty_WhenScalarIsNullOrNonString()
    {
        TenantCreationScalarFlow.ReadUserEmailOrEmpty(null).Should().BeEmpty();
        TenantCreationScalarFlow.ReadUserEmailOrEmpty(123).Should().BeEmpty();
    }
}
