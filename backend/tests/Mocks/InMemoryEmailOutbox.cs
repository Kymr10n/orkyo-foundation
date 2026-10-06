using Api.Repositories;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// In-memory <see cref="IEmailOutboxRepository"/> for tests that exercise <c>EmailService</c>
/// and <c>EmailOutboxDeliverer</c> without a database. Mirrors the SQL semantics that matter:
/// a claim leases the row, marking sent clears the bodies, a failed attempt counts and
/// reschedules or kills the row.
/// </summary>
public sealed class InMemoryEmailOutbox : IEmailOutboxRepository
{
    public sealed class Row
    {
        public required Guid Id { get; init; }
        public required string ToEmail { get; init; }
        public required string Subject { get; init; }
        public string? HtmlBody { get; set; }
        public string? TextBody { get; set; }
        public string Status { get; set; } = "pending";
        public int Attempts { get; set; }
        public DateTime NextAttemptAtUtc { get; set; } = DateTime.MinValue;
        public string? LastError { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Row> _rows = new();
    private readonly TimeProvider _time;

    public InMemoryEmailOutbox(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<Row> Rows { get { lock (_lock) return _rows.Values.ToList(); } }

    public Row Single() => Rows.Should().ContainSingle().Subject;

    public Task<Guid> EnqueueAsync(string toEmail, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        var row = new Row { Id = Guid.NewGuid(), ToEmail = toEmail, Subject = subject, HtmlBody = htmlBody, TextBody = textBody };
        lock (_lock) _rows[row.Id] = row;
        return Task.FromResult(row.Id);
    }

    public Task<OutboxMessage?> ClaimAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(id, out var row) || !IsDue(row)) return Task.FromResult<OutboxMessage?>(null);
            row.NextAttemptAtUtc = Now + EmailOutboxPolicy.ClaimLease;
            return Task.FromResult<OutboxMessage?>(ToMessage(row));
        }
    }

    public Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int batchSize, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var due = _rows.Values.Where(IsDue).OrderBy(r => r.NextAttemptAtUtc).Take(batchSize).ToList();
            foreach (var row in due) row.NextAttemptAtUtc = Now + EmailOutboxPolicy.ClaimLease;
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(due.Select(ToMessage).ToList());
        }
    }

    public Task MarkSentAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_rows.TryGetValue(id, out var row) && row.Status == "pending")
            {
                row.Status = "sent"; row.Attempts++; row.HtmlBody = null; row.TextBody = null; row.LastError = null;
            }
        }
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid id, string error, DateTime nextAttemptAtUtc, bool dead, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_rows.TryGetValue(id, out var row) && row.Status == "pending")
            {
                row.Attempts++; row.LastError = error; row.NextAttemptAtUtc = nextAttemptAtUtc;
                if (dead) row.Status = "dead";
            }
        }
        return Task.CompletedTask;
    }

    public Task<int> PruneAsync(DateTime sentOlderThanUtc, DateTime deadOlderThanUtc, CancellationToken ct = default)
        => Task.FromResult(0);

    private DateTime Now => _time.GetUtcNow().UtcDateTime;
    private bool IsDue(Row row) => row.Status == "pending" && row.NextAttemptAtUtc <= Now;
    private static OutboxMessage ToMessage(Row row) =>
        new(row.Id, row.ToEmail, row.ToEmail, row.Subject, row.HtmlBody ?? "", row.TextBody ?? "", row.Attempts);
}
