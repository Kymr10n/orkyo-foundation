using Api.Helpers;

namespace Api.Tests.Helpers;

public class KeyHelpersTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("My Preset Name", "my-preset-name")]
    [InlineData("UPPER CASE", "upper-case")]
    [InlineData("hello_world", "hello-world")]
    [InlineData("my_key_name", "my-key-name")]
    [InlineData("Hello! World@2025", "hello-world2025")]       // special characters removed
    [InlineData("-leading and trailing-", "leading-and-trailing")]
    [InlineData("simple", "simple")]
    [InlineData("Sprint 2", "sprint-2")]
    [InlineData("", "")]
    [InlineData("!@#$%", "")]                                   // nothing left once stripped
    public void GenerateKey_NormalisesTheName(string input, string expected) =>
        KeyHelpers.GenerateKey(input).Should().Be(expected);
}
