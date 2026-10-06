using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Npgsql;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The deliverer against the real outbox table: a due row goes out and is marked sent, a
/// failing transport leaves it pending with a later attempt, the attempt limit kills it, and
/// a row another sender holds is not sent twice.
/// </summary>
[Collection("Database collection")]
public class EmailOutboxDelivererTests
{
    private readonly IEmailOutboxRepository _repo;
    private readonly string _cp;

    public EmailOutboxDelivererTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IEmailOutboxRepository>();
        _cp = fixture.ControlPlaneConnectionString;
    }

    private EmailOutboxDeliverer CreateDeliverer(Mock<IEmailTransport> transport) =>
        new(_repo, transport.Object, NullLogger<EmailOutboxDeliverer>.Instance, TimeProvider.System);

    private static Mock<IEmailTransport> Transport(bool fails)
    {
        var mock = new Mock<IEmailTransport>();
        var setup = mock.Setup(t => t.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()));
        if (fails) setup.ThrowsAsync(new IOException("relay refused"));
        else setup.Returns(Task.CompletedTask);
        return mock;
    }

    private async Task<(string Status, int Attempts, string? LastError)> ReadRowAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status, attempts, last_error FROM email_outbox WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task SetAttemptsAsync(Guid id, int attempts)
    {
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE email_outbox SET attempts = @n, next_attempt_at = now() WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("n", attempts);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task TryDeliverNow_SendsTheMailAndMarksItSent()
    {
        var transport = Transport(fails: false);
        var id = await _repo.EnqueueAsync("to@example.com", "To", "Now", "<p>now</p>", "now");

        var delivered = await CreateDeliverer(transport).TryDeliverNowAsync(id);

        delivered.Should().BeTrue();
        transport.Verify(t => t.SendAsync(It.Is<MimeMessage>(m => m.Subject == "Now"), It.IsAny<CancellationToken>()), Times.Once);
        (await ReadRowAsync(id)).Status.Should().Be("sent");
    }

    [Fact]
    public async Task TryDeliverNow_WhenTheTransportFails_LeavesTheRowPendingForTheWorker()
    {
        var id = await _repo.EnqueueAsync("to@example.com", "To", "Later", "<p/>", "");

        var delivered = await CreateDeliverer(Transport(fails: true)).TryDeliverNowAsync(id);

        delivered.Should().BeFalse();
        var row = await ReadRowAsync(id);
        row.Status.Should().Be("pending");
        row.Attempts.Should().Be(1);
        row.LastError.Should().Be("relay refused");
    }

    [Fact]
    public async Task DeliverPending_AtTheAttemptLimit_MarksTheRowDead()
    {
        var id = await _repo.EnqueueAsync("to@example.com", "To", "Doomed", "<p/>", "");
        await SetAttemptsAsync(id, EmailOutboxPolicy.MaxAttempts - 1);

        await CreateDeliverer(Transport(fails: true)).DeliverPendingAsync();

        var row = await ReadRowAsync(id);
        row.Status.Should().Be("dead");
        row.Attempts.Should().Be(EmailOutboxPolicy.MaxAttempts);
    }

    [Fact]
    public async Task DeliverPending_SendsEveryDueRowAndSkipsLeasedOnes()
    {
        var transport = Transport(fails: false);
        var due = await _repo.EnqueueAsync("due@example.com", "Due", "Due now", "<p/>", "");
        var leased = await _repo.EnqueueAsync("held@example.com", "Held", "Held elsewhere", "<p/>", "");
        (await _repo.ClaimAsync(leased)).Should().NotBeNull("another sender holds this one");

        await CreateDeliverer(transport).DeliverPendingAsync();

        (await ReadRowAsync(due)).Status.Should().Be("sent");
        (await ReadRowAsync(leased)).Status.Should().Be("pending", "a leased row belongs to its claimer");
        transport.Verify(t => t.SendAsync(It.Is<MimeMessage>(m => m.Subject == "Held elsewhere"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryDeliverNow_OnARowAnotherSenderClaimed_DoesNotSendTwice()
    {
        var transport = Transport(fails: false);
        var id = await _repo.EnqueueAsync("to@example.com", "To", "Once", "<p/>", "");
        (await _repo.ClaimAsync(id)).Should().NotBeNull();

        var delivered = await CreateDeliverer(transport).TryDeliverNowAsync(id);

        delivered.Should().BeFalse();
        transport.Verify(t => t.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
