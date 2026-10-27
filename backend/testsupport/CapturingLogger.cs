using Microsoft.Extensions.Logging;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps what it was told, so a test asserts on the rendered
/// message instead of reproducing the logger's call signature with a mock.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));
}
