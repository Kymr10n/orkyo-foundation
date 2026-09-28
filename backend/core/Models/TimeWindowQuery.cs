namespace Api.Models;

/// <summary>
/// A <c>[from, to)</c> window a caller asks a list or a utilization series for, with the bucket
/// size when the answer is bucketed. Every field is optional so that
/// <c>TimeWindowQueryValidator</c> can refuse a missing one; read <see cref="FromUtc"/> and
/// <see cref="ToUtc"/> only after it passed.
/// </summary>
public sealed record TimeWindowQuery(DateTime? From, DateTime? To, string? Granularity = null)
{
    public DateTime FromUtc => From!.Value;
    public DateTime ToUtc => To!.Value;
}
