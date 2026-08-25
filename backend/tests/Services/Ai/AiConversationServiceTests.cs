using Api.Helpers;
using Api.Repositories;
using Api.Security;
using Api.Services.Ai;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// The layer that decides whose conversation is touched and how big it may be.
///
/// The owner is never a parameter — it comes from the authenticated principal — so these
/// pin that no request shape can select somebody else's rows.
/// </summary>
public class AiConversationServiceTests
{
    private static readonly Guid Caller = Guid.NewGuid();

    private readonly Mock<IAiConversationRepository> _repository = new();
    private readonly Mock<ICurrentPrincipal> _principal = new();
    private readonly AiConversationService _service;

    public AiConversationServiceTests()
    {
        _principal.SetupGet(p => p.UserId).Returns(Caller);
        // A write reports whether it landed; Moq's default of false would mean "that id is
        // someone else's", which is a real case exercised by its own test below.
        _repository
            .Setup(r => r.UpsertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _service = new AiConversationService(_repository.Object, _principal.Object);
    }

    private const string Entries = """[{"kind":"user","text":"hi"}]""";
    private const string Transcript = """[]""";

    [Fact]
    public async Task EveryReadIsScopedToTheCaller()
    {
        await _service.ListAsync();
        await _service.GetAsync(Guid.NewGuid());

        _repository.Verify(r => r.ListAsync(Caller, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.GetAsync(Caller, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EveryWriteIsScopedToTheCaller()
    {
        var id = Guid.NewGuid();

        await _service.SaveAsync(id, "Title", Entries, Transcript);
        await _service.DeleteAsync(id);

        _repository.Verify(r => r.UpsertAsync(Caller, id, "Title", Entries, Transcript, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.DeleteAsync(Caller, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SavingTrimsTheCallersOlderConversations()
    {
        // The cap enforces itself on write; there is no cleanup job to fail unnoticed.
        await _service.SaveAsync(Guid.NewGuid(), "Title", Entries, Transcript);

        _repository.Verify(
            r => r.TrimAsync(Caller, AiConversationService.KeepPerUser, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ATranscriptTooLargeToSendIsTooLargeToSave()
    {
        // Storing one would make an unusable conversation permanent: it would restore on
        // every reload and fail on every send.
        var oversized = new string('x', AiDefaults.MaxTranscriptBytes + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.SaveAsync(Guid.NewGuid(), "Title", Entries, oversized));

        _repository.Verify(r => r.UpsertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnOversizedRenderedLogIsRefusedToo()
    {
        var oversized = new string('x', AiDefaults.MaxTranscriptBytes + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.SaveAsync(Guid.NewGuid(), "Title", oversized, Transcript));
    }

    [Fact]
    public async Task ALongTitleIsShortenedRatherThanRejected()
    {
        // The title is generated from the first message, so refusing a long one would
        // fail a save the person did nothing wrong to cause.
        await _service.SaveAsync(Guid.NewGuid(), new string('t', 500), Entries, Transcript);

        _repository.Verify(r => r.UpsertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.Is<string>(t => t.Length == AiConversationService.MaxTitleLength),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnEmptyTitleGetsAPlaceholder()
    {
        await _service.SaveAsync(Guid.NewGuid(), "   ", Entries, Transcript);

        _repository.Verify(r => r.UpsertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            "Conversation", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ASaveTheDatabaseRefusedIsReportedAsAFailure()
    {
        // The guard rejects a write whose id belongs to someone else, and Postgres raises
        // nothing for it. Returning success here would tell the client its conversation is
        // saved when the next read comes back empty.
        _repository
            .Setup(r => r.UpsertAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.SaveAsync(Guid.NewGuid(), "Title", Entries, Transcript));

        _repository.Verify(r => r.TrimAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
