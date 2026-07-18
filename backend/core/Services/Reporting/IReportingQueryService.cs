using Api.Models.Reporting;

namespace Api.Services.Reporting;

public interface IReportingQueryService
{
    Task<ReportingResult<SpaceUtilizationRow>> GetSpaceUtilizationAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<ResourceUtilizationRow>> GetResourceUtilizationAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<AllocationRow>> GetAllocationsAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<RequestThroughputRow>> GetRequestThroughputAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<ConflictRow>> GetConflictsAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<AbsenceRow>> GetAbsencesAsync(
        TenantContext tenant, ReportingQuery query, bool peopleLevelEnabled,
        CancellationToken ct = default);

    Task<ReportingResult<CapacityVsDemandRow>> GetCapacityVsDemandAsync(
        TenantContext tenant, ReportingQuery query, CancellationToken ct = default);
}
