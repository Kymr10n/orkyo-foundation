namespace Api.Models;

/// <summary>
/// A <c>[from, to)</c> window a caller asks a list or a utilization series for, with the bucket
/// size when the answer is bucketed. Every field is optional so that
/// <c>TimeWindowQueryValidator</c> can refuse a missing one; read <see cref="FromValue"/> and
/// <see cref="ToValue"/> only after it passed.
/// </summary>
public sealed record TimeWindowQuery(DateTime? From, DateTime? To, string? Granularity = null)
{
    public DateTime FromValue => From!.Value;
    public DateTime ToValue => To!.Value;
}
