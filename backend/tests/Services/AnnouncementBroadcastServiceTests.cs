using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Integration tests for <see cref="AnnouncementBroadcastService"/> — the worker path that emails the
/// 'email' channel of announcements to all active, non-opted-out users (then marks them sent).
/// </summary>
[Collection("Database collection")]
public class AnnouncementBroadcastServiceTests
{
    private readonly DatabaseFixture _fixture;
    private readonly string _conn;
    private readonly IAnnouncementRepository _repository;
    private readonly AnnouncementBroadcastService _service;
    private readonly MockEmailService _email;

    public AnnouncementBroadcastServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _conn = $"Host=localhost;Port={_fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
        var scope = fixture.Factory.Services.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _email = fixture.Factory.MockEmailService;
        _service = new AnnouncementBroadcastService(_repository, _email, dbFactory,
            NullLogger<AnnouncementBroadcastService>.Instance);
    }

    private async Task<Guid> CreateUserAsync(string prefix, bool active = true, bool optedOut = false)
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status, announcement_email_opt_out) " +
            "VALUES (@id, @email, 'U', @status, @opt)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", $"{prefix}-{id}@test.com");
        cmd.Parameters.AddWithValue("status", active ? "active" : "disabled");
        cmd.Parameters.AddWithValue("opt", optedOut);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<AnnouncementDto> CreateAnnouncementAsync(Guid authorId, string[] channels, bool important = false)
        => await _repository.CreateAsync(new Announcement
        {
            Id = Guid.NewGuid(),
            Title = "Broadcast title",
            Body = "Broadcast body",
            Channels = channels,
            IsImportant = important,
            CreatedByUserId = authorId,
            UpdatedByUserId = authorId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });

    private async Task<long> CountActiveRecipientsAsync()
    {
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE status = 'active' AND announcement_email_opt_out = false", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> EmailSentAsync(Guid announcementId)
    {
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT email_sent_at IS NOT NULL FROM announcements WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", announcementId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task CleanAnnouncementsAsync()
    {
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM announcement_reads; DELETE FROM announcements", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ProcessPendingBroadcasts_EmailsActiveUsers_ExcludesOptedOut_AndMarksSent()
    {
        await CleanAnnouncementsAsync();
        _email.Reset();

        var author = await CreateUserAsync("author");
        var activeId = await CreateUserAsync("active");
        var optedOutId = await CreateUserAsync("optout", optedOut: true);
        var activeEmail = $"active-{activeId}@test.com";
        var optedOutEmail = $"optout-{optedOutId}@test.com";

        var announcement = await CreateAnnouncementAsync(author, [AnnouncementChannels.Email]);
        var expectedRecipients = await CountActiveRecipientsAsync();

        await _service.ProcessPendingBroadcastsAsync();

        _email.SendAnnouncementCallCount.Should().Be((int)expectedRecipients);
        _email.Recipients.Should().Contain(activeEmail);
        _email.Recipients.Should().NotContain(optedOutEmail);
        (await EmailSentAsync(announcement.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPendingBroadcasts_ImportantAnnouncement_EmailsOptedOutUsersToo()
    {
        await CleanAnnouncementsAsync();
        _email.Reset();

        var author = await CreateUserAsync("author-imp");
        var optedOutId = await CreateUserAsync("optout-imp", optedOut: true);
        var optedOutEmail = $"optout-imp-{optedOutId}@test.com";

        await CreateAnnouncementAsync(author, [AnnouncementChannels.Email], important: true);

        await _service.ProcessPendingBroadcastsAsync();

        // Important announcements are mandatory — opted-out users are still emailed.
        _email.Recipients.Should().Contain(optedOutEmail);
    }

    [Fact]
    public async Task ProcessPendingBroadcasts_AlreadySent_DoesNotResend()
    {
        await CleanAnnouncementsAsync();
        var author = await CreateUserAsync("author2");
        await CreateAnnouncementAsync(author, [AnnouncementChannels.Email]);

        await _service.ProcessPendingBroadcastsAsync(); // first run marks sent
        _email.Reset();
        await _service.ProcessPendingBroadcastsAsync(); // second run: nothing pending

        _email.SendAnnouncementCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessPendingBroadcasts_SiteOnlyAnnouncement_IsNotEmailed()
    {
        await CleanAnnouncementsAsync();
        _email.Reset();
        var author = await CreateUserAsync("author3");
        await CreateAnnouncementAsync(author, [AnnouncementChannels.Site]);

        await _service.ProcessPendingBroadcastsAsync();

        _email.SendAnnouncementCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetActiveForUser_ExcludesEmailOnlyAnnouncements()
    {
        await CleanAnnouncementsAsync();
        var author = await CreateUserAsync("author4");
        var site = await CreateAnnouncementAsync(author, [AnnouncementChannels.Site]);
        await CreateAnnouncementAsync(author, [AnnouncementChannels.Email]); // email-only — must not show in-app

        var active = await _repository.GetActiveForUserAsync(author);

        active.Select(a => a.Id).Should().Contain(site.Id);
        active.Should().HaveCount(1);
    }
}
