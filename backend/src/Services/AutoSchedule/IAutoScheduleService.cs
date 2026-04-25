using Api.Models;

namespace Api.Services.AutoSchedule;

public interface IAutoScheduleService
{
    Task<AutoSchedulePreviewResponse> PreviewAsync(
        AutoSchedulePreviewRequest request, CancellationToken cancellationToken);

    Task<AutoScheduleApplyResponse> ApplyAsync(
        AutoScheduleApplyRequest request, CancellationToken cancellationToken);
}
