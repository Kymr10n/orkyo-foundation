namespace Api.Services;

/// <summary>
/// The worker's liveness signal. <see cref="FoundationWorkerLoop"/> touches
/// <see cref="Path"/> after every cycle; the container HEALTHCHECK fails when the file is
/// older than a few minutes. A process check (<c>pgrep</c>) only proved the process existed —
/// a loop stuck on a hung connection stayed "healthy" indefinitely.
/// </summary>
public static class WorkerHeartbeat
{
    /// <summary>Same path in every worker image; the Dockerfile HEALTHCHECK reads it.</summary>
    public const string Path = "/tmp/orkyo-worker.heartbeat";

    /// <summary>Best effort: a heartbeat that cannot be written must never fail the loop.</summary>
    public static void Touch()
    {
        try
        {
            System.IO.File.WriteAllText(Path, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
