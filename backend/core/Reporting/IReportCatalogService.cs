namespace Api.Reporting;

public interface IReportCatalogService
{
    /// <summary>
    /// Returns the reports visible to the caller given their current role.
    /// Never throws — returns an empty list when no reports are accessible
    /// or when reporting is disabled.
    /// </summary>
    IReadOnlyList<ReportDefinition> GetVisibleReports();
}
