using System.Text.Json;
using Api.Models;
using Api.Repositories;
using Api.Services.Ai;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// The assistant's entity resolution. It wraps the same search the command palette uses, so
/// the rules that matter here are the ones the wrapper adds: what reaches the index, and
/// what the model reads back.
/// </summary>
public class SearchToolTests
{
    private readonly Mock<ISearchRepository> _search = new();

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement;

    private static SearchResult Result(string type, string title, string? resourceTypeKey = null) => new()
    {
        Type = type,
        Id = Guid.NewGuid(),
        Title = title,
        Subtitle = "at Precision Machining",
        SiteId = Guid.NewGuid(),
        ResourceTypeKey = resourceTypeKey,
        Permissions = new SearchResultPermissions { CanRead = true, CanEdit = false },
    };

    private SearchTool CreateSut(params SearchResult[] results)
    {
        _search
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string[]?>(),
                                      It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results.ToList());
        return new SearchTool(_search.Object);
    }

    [Fact]
    public async Task ReturnsTheFieldsNeededToActOnAMatch()
    {
        // id to read or open it, type to know which tool takes it, name to say it back.
        var sut = CreateSut(Result("resource", "Mill 3", resourceTypeKey: "mill"));

        var json = await sut.ExecuteAsync(Input("""{"query":"mill"}"""), default);

        Assert.Contains("\"type\":\"resource\"", json);
        Assert.Contains("\"name\":\"Mill 3\"", json);
        Assert.Contains("\"resourceType\":\"mill\"", json);
        Assert.Contains("\"id\":", json);
    }

    [Fact]
    public async Task SaysNothingMatchedRatherThanReturningAnEmptyList()
    {
        // "[]" invites the model to guess an id; a sentence does not.
        var sut = CreateSut();

        var json = await sut.ExecuteAsync(Input("""{"query":"turbine 4711"}"""), default);

        Assert.Contains("Nothing matches", json);
        Assert.Contains("turbine 4711", json);
    }

    [Fact]
    public async Task RefusesASearchWithNothingToSearchFor()
    {
        var sut = CreateSut();

        var json = await sut.ExecuteAsync(Input("""{"query":"  "}"""), default);

        Assert.Contains("needs something to search for", json);
        _search.Verify(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string[]?>(),
                                          It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("""{"query":"x"}""", 10)]
    [InlineData("""{"query":"x","limit":3}""", 3)]
    [InlineData("""{"query":"x","limit":500}""", 25)]
    [InlineData("""{"query":"x","limit":-4}""", 1)]
    public async Task ClampsTheLimitTheModelAsksFor(string input, int expected)
    {
        var sut = CreateSut();

        await sut.ExecuteAsync(Input(input), default);

        _search.Verify(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string[]?>(),
                                          expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PassesThroughTheKindsTheIndexKnows()
    {
        var sut = CreateSut();

        await sut.ExecuteAsync(Input("""{"query":"x","types":["request","site"]}"""), default);

        _search.Verify(s => s.SearchAsync("x", null,
            It.Is<string[]>(t => t.Length == 2 && t.Contains("request") && t.Contains("site")),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DropsAKindTheIndexDoesNotHave_AndSearchesEverything()
    {
        // Passing an invented type through would return nothing, which the model would read
        // as "no such record" rather than "not a kind of record".
        var sut = CreateSut();

        await sut.ExecuteAsync(Input("""{"query":"x","types":["allocation"]}"""), default);

        _search.Verify(s => s.SearchAsync("x", null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchesEverySiteBecauseThePersonMayBeAskingAboutAnother()
    {
        var sut = CreateSut();

        await sut.ExecuteAsync(Input("""{"query":"x"}"""), default);

        _search.Verify(s => s.SearchAsync(It.IsAny<string>(), null, It.IsAny<string[]?>(),
                                          It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TheDescriptionForbidsInventingIds()
    {
        // The model's only defence against fabricating an id is being told not to.
        Assert.Contains("Never invent an id", new SearchTool(_search.Object).Definition.Description);
    }
}
