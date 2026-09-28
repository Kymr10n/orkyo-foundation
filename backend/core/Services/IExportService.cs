using Api.Models.Export;

namespace Api.Services;

/// <summary>Produces the tenant's JSON data export: sites, criteria, groups, templates, requests and resources.</summary>
public interface IExportService
{
    /// <summary>
    /// Runs the export described by <paramref name="request"/> and returns the payload
    /// (schema version, provenance, data) that the endpoint serializes as JSON.
    /// </summary>
    Task<ExportPayload> ExportAsync(ExportRequest request, CancellationToken ct = default);
}
