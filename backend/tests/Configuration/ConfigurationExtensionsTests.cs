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
    public void GetRequiredInt_Throws_WhenValueIsNotAnInteger()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_PORT"] = "not-a-number"
        });

        var act = () => config.GetRequiredInt("SMTP_PORT");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*valid integer*");
    }

    [Fact]
    public void GetRequiredBool_ReturnsTrue_WhenValueIsTrue()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_USE_SSL"] = "true"
        });

        var value = config.GetRequiredBool("SMTP_USE_SSL");

        value.Should().BeTrue();
    }

    [Fact]
    public void GetRequiredBool_ReturnsFalse_WhenValueIsFalse()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_USE_SSL"] = "false"
        });

        var value = config.GetRequiredBool("SMTP_USE_SSL");

        value.Should().BeFalse();
    }

    [Fact]
    public void GetOptionalString_ReturnsEmpty_WhenMissing()
    {
        var config = BuildConfig();

        var value = config.GetOptionalString("SMTP_USERNAME");

        value.Should().BeEmpty();
    }

    [Fact]
    public void IsSet_ReturnsTrue_WhenValueIsPresent()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "localhost"
        });

        config.IsSet("SMTP_HOST").Should().BeTrue();
    }

    [Fact]
    public void IsSet_ReturnsFalse_WhenValueIsMissing()
    {
        var config = BuildConfig();

        config.IsSet("SMTP_HOST").Should().BeFalse();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }
}
