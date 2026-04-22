using Api.Helpers;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Helpers;

public class KeyHelpersTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("My Preset Name", "my-preset-name")]
    [InlineData("UPPER CASE", "upper-case")]
    public void GenerateKey_ShouldLowercaseAndReplaceSpacesWithDashes(string input, string expected) =>
        KeyHelpers.GenerateKey(input).Should().Be(expected);

    [Theory]
    [InlineData("hello_world", "hello-world")]
    [InlineData("my_key_name", "my-key-name")]
    public void GenerateKey_ShouldReplaceUnderscoresWithDashes(string input, string expected) =>
        KeyHelpers.GenerateKey(input).Should().Be(expected);

    [Fact]
    public void GenerateKey_ShouldRemoveSpecialCharacters()
    {
        KeyHelpers.GenerateKey("Hello! World@2025").Should().Be("hello-world2025");
    }

    [Fact]
    public void GenerateKey_ShouldTrimLeadingAndTrailingDashes()
    {
        KeyHelpers.GenerateKey("-leading and trailing-").Should().Be("leading-and-trailing");
    }

    [Fact]
    public void GenerateKey_ShouldHandleAlreadyValidKey()
    {
        KeyHelpers.GenerateKey("simple").Should().Be("simple");
    }

    [Fact]
    public void GenerateKey_ShouldHandleNumbersInName()
    {
        KeyHelpers.GenerateKey("Sprint 2").Should().Be("sprint-2");
    }

    [Fact]
    public void GenerateKey_ShouldReturnEmptyString_ForEmptyInput()
    {
        KeyHelpers.GenerateKey("").Should().Be("");
    }

    [Fact]
    public void GenerateKey_ShouldReturnEmpty_WhenAllCharactersStripped()
    {
        KeyHelpers.GenerateKey("!@#$%").Should().Be("");
    }
}
