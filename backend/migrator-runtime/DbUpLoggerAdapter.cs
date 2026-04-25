using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace Orkyo.Migrator;

/// <summary>
/// Bridges DbUp's <see cref="IUpgradeLog"/> to <see cref="ILogger"/> so all migration
/// output flows through the host's standard logging pipeline.
/// </summary>
internal sealed class DbUpLoggerAdapter : IUpgradeLog
{
    private readonly ILogger _logger;

    public DbUpLoggerAdapter(ILogger logger)
    {
        _logger = logger;
    }

    public void LogTrace(string format, params object[] args) => _logger.LogTrace(format, args);
    public void LogDebug(string format, params object[] args) => _logger.LogDebug(format, args);
    public void LogInformation(string format, params object[] args) => _logger.LogInformation(format, args);
    public void LogWarning(string format, params object[] args) => _logger.LogWarning(format, args);
    public void LogError(string format, params object[] args) => _logger.LogError(format, args);
    public void LogError(Exception ex, string format, params object[] args) => _logger.LogError(ex, format, args);
}
