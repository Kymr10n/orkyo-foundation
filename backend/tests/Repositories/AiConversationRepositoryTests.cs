using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Saved conversations, exercised against a real database.
///
/// The property that matters here is ownership: a transcript quotes workspace data and is
/// somebody's working notes, so another member reading it — or overwriting it by guessing
/// an id — would be a real breach, not an inconvenience.
/// </summary>
[Collection("Database collection")]
public class AiConversationRepositoryTests
{
    private readonly IAiConversationRepository _repo;

    public AiConversationRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IAiConversationRepository>();
    }

    private const string Entries = """[{"kind":"user","text":"hello"}]""";
    private const string Transcript = """[{"role":"user","blocks":[]}]""";

    [Fact]
    public async Task ARoundTripReturnsWhatWasSaved()
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();

        await _repo.UpsertAsync(owner, id, "First question", Entries, Transcript);
        var found = await _repo.GetAsync(owner, id);

        Assert.NotNull(found);
        Assert.Equal("First question", found!.Title);
        Assert.Contains("hello", found.Entries.GetRawText());
    }

    [Fact]
    public async Task SavingTwiceReplacesRatherThanDuplicating()
    {
        // Each turn rewrites the whole conversation, so the same id arrives repeatedly.
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();

        await _repo.UpsertAsync(owner, id, "Draft", Entries, Transcript);
        await _repo.UpsertAsync(owner, id, "Revised", Entries, Transcript);

        var all = await _repo.ListAsync(owner);
        Assert.Single(all);
        Assert.Equal("Revised", all[0].Title);
    }

    [Fact]
    public async Task OneMemberCannotReadAnothersConversation()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var id = Guid.NewGuid();
        await _repo.UpsertAsync(owner, id, "Private", Entries, Transcript);

        Assert.Null(await _repo.GetAsync(stranger, id));
        Assert.DoesNotContain(await _repo.ListAsync(stranger), c => c.Id == id);
    }

    [Fact]
    public async Task OneMemberCannotDeleteAnothersConversation()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var id = Guid.NewGuid();
        await _repo.UpsertAsync(owner, id, "Private", Entries, Transcript);

        Assert.False(await _repo.DeleteAsync(stranger, id));
        Assert.NotNull(await _repo.GetAsync(owner, id));
    }

    [Fact]
    public async Task OneMemberCannotOverwriteAnothersConversationByGuessingItsId()
    {
        // The upsert's WHERE clause is what stops this: a conflicting id owned by someone
        // else updates nothing rather than silently taking the row.
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var id = Guid.NewGuid();
        await _repo.UpsertAsync(owner, id, "Mine", Entries, Transcript);

        await _repo.UpsertAsync(stranger, id, "Hijacked", Entries, Transcript);

        var found = await _repo.GetAsync(owner, id);
        Assert.Equal("Mine", found!.Title);
        Assert.Null(await _repo.GetAsync(stranger, id));
    }

    [Fact]
    public async Task TrimKeepsTheNewestAndDropsTheRest()
    {
        var owner = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            await _repo.UpsertAsync(owner, id, $"Conversation {i}", Entries, Transcript);
            // updated_at is NOW() to the microsecond; without a gap the ordering that
            // decides what survives would be arbitrary.
            await Task.Delay(5);
        }

        var removed = await _repo.TrimAsync(owner, keep: 2);

        Assert.Equal(3, removed);
        var remaining = await _repo.ListAsync(owner);
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, c => c.Id == ids[4]);
        Assert.Contains(remaining, c => c.Id == ids[3]);
    }

    [Fact]
    public async Task TrimLeavesOtherMembersAlone()
    {
        var owner = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        await _repo.UpsertAsync(owner, Guid.NewGuid(), "A", Entries, Transcript);
        await _repo.UpsertAsync(owner, Guid.NewGuid(), "B", Entries, Transcript);
        var neighbourId = Guid.NewGuid();
        await _repo.UpsertAsync(neighbour, neighbourId, "Theirs", Entries, Transcript);

        await _repo.TrimAsync(owner, keep: 1);

        Assert.NotNull(await _repo.GetAsync(neighbour, neighbourId));
    }

    [Fact]
    public async Task TheListIsNewestFirst()
    {
        var owner = Guid.NewGuid();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await _repo.UpsertAsync(owner, older, "Older", Entries, Transcript);
        await Task.Delay(5);
        await _repo.UpsertAsync(owner, newer, "Newer", Entries, Transcript);

        var all = await _repo.ListAsync(owner);

        Assert.Equal(newer, all[0].Id);
    }
}
