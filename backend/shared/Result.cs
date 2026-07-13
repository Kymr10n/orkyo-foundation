namespace Orkyo.Shared;

/// <summary>
/// Lightweight success/error result for operations that previously returned
/// <c>(bool success, string? error)</c> tuples. Use <see cref="Ok"/> / <see cref="Fail"/>.
/// </summary>
public readonly record struct Result(bool Success, string? Error)
{
    public static Result Ok() => new(true, null);
    public static Result Fail(string error) => new(false, error);
}
