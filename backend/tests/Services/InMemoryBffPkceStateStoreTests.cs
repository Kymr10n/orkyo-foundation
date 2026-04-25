using Api.Services.BffSession;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class InMemoryBffPkceStateStoreTests
{
    private readonly InMemoryBffPkceStateStore _store = new();

    private static PkceStateData SampleState(string returnTo = "https://orkyo.com/")
        => new("verifier-abc", returnTo);

    [Fact]
    public async Task SetAndGetAndRemove_RoundTrip()
    {
        const string state = "state-1";
        await _store.SetAsync(state, SampleState(), TimePolicyConstants.BffPkceStateTtl);
        var result = await _store.GetAndRemoveAsync(state);
        result.Should().NotBeNull();
        result!.CodeVerifier.Should().Be("verifier-abc");
        result.ReturnTo.Should().Be("https://orkyo.com/");
    }

    [Fact]
    public async Task GetAndRemove_ReturnsNull_WhenNotFound() =>
        (await _store.GetAndRemoveAsync("nonexistent")).Should().BeNull();

    [Fact]
    public async Task GetAndRemove_ReturnsNull_WhenExpired()
    {
        await _store.SetAsync("expired-state", SampleState(), TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);
        (await _store.GetAndRemoveAsync("expired-state")).Should().BeNull();
    }

    [Fact]
    public async Task GetAndRemove_IsOneTimeUse()
    {
        const string state = "one-time-state";
        await _store.SetAsync(state, SampleState(), TimePolicyConstants.BffPkceStateTtl);
        var first = await _store.GetAndRemoveAsync(state);
        var second = await _store.GetAndRemoveAsync(state);
        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task GetAndRemove_ConcurrentCalls_OnlyOneSucceeds()
    {
        const string state = "concurrent-state";
        await _store.SetAsync(state, SampleState(), TimePolicyConstants.BffPkceStateTtl);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _store.GetAndRemoveAsync(state)));
        results.Count(r => r is not null).Should().Be(1, "exactly one concurrent caller should receive the state");
    }
}
