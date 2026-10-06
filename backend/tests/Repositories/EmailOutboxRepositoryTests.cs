using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// The mail outbox against a real database. What matters: a claim is exclusive for its
/// lease, delivery clears the bodies that carry live tokens, a failure reschedules or kills
/// the row, and pruning touches only settled rows.
/// </summary>
[Collection("Database collection")]
public class EmailOutboxRepositoryTests
{
    private readonly IEmailOutboxRepository _repo;
    private readonly string _cp;

    public EmailOutboxRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IEmailOutboxRepository>();
        _cp = fixture.ControlPlaneConnectionString;
    }

    private async Task<(string Status, int Attempts, string? Html, string? LastError, DateTime NextAttempt)> ReadRowAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status, attempts, html_body, last_error, next_attempt_at FROM email_outbox WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the row must exist");
        return (reader.GetString(0), reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetDateTime(4));
    }

    private async Task<bool> RowExistsAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM email_outbox WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return (long)(await cmd.ExecuteScalarAsync())! == 1;
    }

    private async Task AgeRowAsync(Guid id, string status, int daysAgo)
    {
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE email_outbox
            SET status = @status,
                created_at = now() - make_interval(days => @days),
                sent_at = CASE WHEN @status = 'sent' THEN now() - make_interval(days => @days) END
            WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("days", daysAgo);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Enqueue_ThenClaim_ReturnsTheMailOnceUntilTheLeaseRunsOut()
    {
        var id = await _repo.EnqueueAsync("a@example.com", "A", "Hello", "<p>hi</p>", "hi");

        var first = await _repo.ClaimAsync(id);
        var second = await _repo.ClaimAsync(id);

        first.Should().NotBeNull();
        first!.Subject.Should().Be("Hello");
        first.HtmlBody.Should().Be("<p>hi</p>");
        first.Attempts.Should().Be(0);
        second.Should().BeNull("the first claim leased the row");
        var row = await ReadRowAsync(id);
        row.Status.Should().Be("pending");
        row.NextAttempt.Should().BeAfter(DateTime.UtcNow.Add(EmailOutboxPolicy.ClaimLease - TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task ClaimDue_SkipsRowsThatAreNotDueYet()
    {
        var due = await _repo.EnqueueAsync("due@example.com", "D", "Due", "<p/>", "");
        var later = await _repo.EnqueueAsync("later@example.com", "L", "Later", "<p/>", "");
        await _repo.MarkFailedAsync(later, "relay down", DateTime.UtcNow.AddHours(1), dead: false);

        var claimed = await _repo.ClaimDueAsync(1000);

        claimed.Select(m => m.Id).Should().Contain(due).And.NotContain(later);
    }

    [Fact]
    public async Task MarkSent_ClearsTheBodies()
    {
        var id = await _repo.EnqueueAsync("a@example.com", "A", "Invite", "<a href=token>x</a>", "token");

        await _repo.MarkSentAsync(id);

        var row = await ReadRowAsync(id);
        row.Status.Should().Be("sent");
        row.Attempts.Should().Be(1);
        row.Html.Should().BeNull("the body carried a live token");
    }

    [Fact]
    public async Task MarkFailed_ReschedulesThenKills()
    {
        var id = await _repo.EnqueueAsync("a@example.com", "A", "Retry", "<p/>", "");
        var retryAt = DateTime.UtcNow.AddMinutes(5);

        await _repo.MarkFailedAsync(id, "first failure", retryAt, dead: false);
        var afterFirst = await ReadRowAsync(id);
        await _repo.MarkFailedAsync(id, "final failure", retryAt, dead: true);
        var afterDead = await ReadRowAsync(id);
        await _repo.MarkSentAsync(id);
        var afterSentAttempt = await ReadRowAsync(id);

        afterFirst.Status.Should().Be("pending");
        afterFirst.Attempts.Should().Be(1);
        afterFirst.LastError.Should().Be("first failure");
        afterFirst.NextAttempt.Should().BeCloseTo(retryAt, TimeSpan.FromSeconds(1));
        afterDead.Status.Should().Be("dead");
        afterDead.Attempts.Should().Be(2);
        afterSentAttempt.Status.Should().Be("dead", "a settled row is never reopened");
    }

    [Fact]
    public async Task Prune_RemovesOnlyOldSettledRows()
    {
        var oldSent = await _repo.EnqueueAsync("a@example.com", "A", "old sent", "<p/>", "");
        var oldDead = await _repo.EnqueueAsync("a@example.com", "A", "old dead", "<p/>", "");
        var freshSent = await _repo.EnqueueAsync("a@example.com", "A", "fresh sent", "<p/>", "");
        var oldPending = await _repo.EnqueueAsync("a@example.com", "A", "old pending", "<p/>", "");
        await AgeRowAsync(oldSent, "sent", daysAgo: 3);
        await AgeRowAsync(oldDead, "dead", daysAgo: 40);
        await AgeRowAsync(freshSent, "sent", daysAgo: 0);
        await AgeRowAsync(oldPending, "pending", daysAgo: 40);

        var now = DateTime.UtcNow;
        await _repo.PruneAsync(now - EmailOutboxPolicy.SentRetention, now - EmailOutboxPolicy.DeadRetention);

        (await RowExistsAsync(oldSent)).Should().BeFalse();
        (await RowExistsAsync(oldDead)).Should().BeFalse();
        (await RowExistsAsync(freshSent)).Should().BeTrue();
        (await RowExistsAsync(oldPending)).Should().BeTrue("a pending row is never pruned, however old");
    }
}
