using Api.Helpers;
using Microsoft.AspNetCore.Http;

namespace Orkyo.Foundation.Tests.Helpers;

public class IfMatchTests
{
    private static uint? Parse(string? header)
    {
        var request = new DefaultHttpContext().Request;
        if (header is not null) request.Headers.IfMatch = header;
        return IfMatch.Parse(request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    public void NoHeaderOrWildcard_MeansNoCheck(string? header)
        => Parse(header).Should().BeNull();

    [Theory]
    [InlineData("\"0\"", 0u)]
    [InlineData("\"123\"", 123u)]
    [InlineData(" \"4294967295\" ", uint.MaxValue)]
    public void QuotedNumber_IsTheVersion(string header, uint expected)
        => Parse(header).Should().Be(expected);

    [Theory]
    [InlineData("123")]
    [InlineData("W/\"123\"")]
    [InlineData("\"1\", \"2\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"4294967296\"")]
    [InlineData("\"\"")]
    public void AnythingElse_IsAClientError(string header)
        => FluentActions.Invoking(() => Parse(header)).Should().Throw<ArgumentException>();
}
