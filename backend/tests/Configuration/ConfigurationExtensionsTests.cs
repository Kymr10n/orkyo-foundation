using Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace Orkyo.Foundation.Tests.Configuration;

public class ConfigurationExtensionsTests
{
    [Fact]
    public void GetRequired_ReturnsValue_WhenPresent()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "localhost"
        });

        var value = config.GetRequired("SMTP_HOST");

        value.Should().Be("localhost");
    }

    [Fact]
    public void GetRequired_Throws_WhenMissing()
    {
        var config = BuildConfig();

        var act = () => config.GetRequired("SMTP_HOST");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SMTP_HOST*");
    }

    [Fact]
    public void GetRequiredInt_ReturnsParsedInteger_WhenValid()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_PORT"] = "1025"
        });

        var value = config.GetRequiredInt("SMTP_PORT");

        value.Should().Be(1025);
    }

    [Fact]
    public void GetRequiredBool_Throws_WhenInvalid()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_USE_SSL"] = "yes"
        });

        var act = () => config.GetRequiredBool("SMTP_USE_SSL");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*true*false*");
    }

    [Fact]
    public void GetOptionalString_ReturnsEmpty_WhenMissing()
    {
        var config = BuildConfig();

        var value = config.GetOptionalString("SMTP_USERNAME");

        value.Should().BeEmpty();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }
}
