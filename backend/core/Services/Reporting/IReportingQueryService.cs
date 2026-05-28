using Api.Models.Reporting;

namespace Api.Services.Reporting;

public interface IReportingQueryService
{
    Task<ReportingResult<SpaceUtilizationRow>> GetSpaceUtilizationAsync(
        Guid tenantId, TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<ResourceUtilizationRow>> GetResourceUtilizationAsync(
        Guid tenantId, TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<AllocationRow>> GetAllocationsAsync(
        Guid tenantId, TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<RequestThroughputRow>> GetRequestThroughputAsync(
        Guid tenantId, TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<ConflictRow>> GetConflictsAsync(
        Guid tenantId, TenantContext tenant, ReportingQuery query, CancellationToken ct = default);

    Task<ReportingResult<AbsenceRow>> GetAbsencesAsync(
        Guid tenantId, TenantContext tenant, ReportingQuery query, bool peopleLevelEnabled,
        CancellationToken ct = default);

    Task<ReportingResult<CapacityVsDemandRow>> GetCapacityVsDemandAsync(
        Guid tenantId, TenantContext tenant, ReportingQuery query, CancellationToken ct = default);
}
