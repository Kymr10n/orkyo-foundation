using Api.Endpoints;
using Api.Endpoints.Admin;
using Microsoft.AspNetCore.Builder;

namespace Api.Configuration;

public static class FoundationEndpointExtensions
{
    /// <summary>
    /// Maps all foundation-owned endpoints. Products call this once and add only their
    /// product-specific endpoints on top.
    /// </summary>
    public static WebApplication MapFoundationEndpoints(this WebApplication app)
    {
        // Auth / BFF
        app.MapBffAuthEndpoints();
        app.MapSessionEndpoints();
        app.MapAccountLifecycleEndpoints();

        // Admin
        app.MapAuditEndpoints();
        app.MapDiagnosticsAdminEndpoints();
        app.MapSettingsAdminEndpoints();
        app.MapUserAdminEndpoints();

        // Features
        app.MapAnnouncementEndpoints();
        app.MapAutoScheduleEndpoints();
        app.MapContactEndpoints();
        app.MapCriteriaEndpoints();
        app.MapCriterionApplicabilityEndpoints();
        app.MapDepartmentEndpoints();
        app.MapExportEndpoints();
        app.MapFeedbackEndpoints();
        app.MapFloorplanEndpoints();
        app.MapGroupCapabilityEndpoints();
        app.MapJobTitleEndpoints();
        app.MapPersonProfileEndpoints();
        app.MapPresetEndpoints();
        app.MapRequestEndpoints();
        app.MapResourceAssignmentEndpoints();
        app.MapResourceEndpoints();
        app.MapResourceGroupEndpoints();
        app.MapResourceGroupMemberEndpoints();
        app.MapResourceTypeEndpoints();
        app.MapSchedulingEndpoints();
        app.MapSearchEndpoints();
        app.MapSecurityEndpoints();
        app.MapSettingsEndpoints();
        app.MapSiteEndpoints();
        app.MapSpaceEndpoints();
        app.MapTemplateEndpoints();
        app.MapUserAnnouncementEndpoints();
        app.MapUserManagementEndpoints();
        app.MapUserPreferencesEndpoints();
        app.MapUtilizationEndpoints();

        return app;
    }
}
