using Api.Models.Export;

namespace Api.Services;

public interface IExportService
{
    Task<ExportPayload> ExportAsync(ExportRequest request);
}
