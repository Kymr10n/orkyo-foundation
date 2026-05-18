using Api.Models.Export;

namespace Api.Services;

/// <summary>Produces data exports (CSV, JSON) for requests, assignments, and conflicts.</summary>
public interface IExportService
{
    /// <summary>
    /// Runs the export described by <paramref name="request"/> and returns the payload
    /// (filename, content type, byte content) ready to stream back as a file download.
    /// </summary>
    Task<ExportPayload> ExportAsync(ExportRequest request, CancellationToken ct = default);
}
