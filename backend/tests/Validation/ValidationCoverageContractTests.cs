using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Validation;

/// <summary>
/// The validation-coverage conformance test (#96), mirroring
/// <see cref="Authorization.AuthorizationContractTests"/>: it enumerates the live
/// endpoint graph and asserts that every mutating <c>/api</c> route whose handler
/// binds a <c>*Request</c> body model also injects the matching
/// <c>IValidator&lt;TRequest&gt;</c>. Add a new write endpoint with a request model
/// and no validator and this test fails.
///
/// <para>The allowlist below is a BURN-DOWN, not an exemption policy: it froze the
/// uncovered routes at the moment the ratchet was introduced. Fix one by injecting
/// its validator and DELETE its row — never add rows for new endpoints.</para>
/// </summary>
[Collection("Database collection")]
public class ValidationCoverageContractTests
{
    private readonly DatabaseFixture _fixture;

    public ValidationCoverageContractTests(DatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Routes that bound a *Request model without a validator when this ratchet was
    /// introduced (2026-07-28). Burn these down; never grow this list.
    /// </summary>
    private static readonly string[] UncoveredWhenIntroduced =
    {
        "PATCH /api/admin/feedback/{id:guid} (UpdateFeedbackRequest)",
        "POST /api/admin/announcements/ (CreateAnnouncementRequest)",
        "POST /api/admin/export/ (ExportRequest)",
        "POST /api/person-profiles/{resourceId:guid}/link (LinkUserToPersonProfileRequest)",
        "POST /api/reporting/v1/tokens/ (CreateReportingTokenRequest)",
        "POST /api/requests/{id:guid}/requirements (AddRequirementRequest)",
        "POST /api/resource-assignments/ (CreateResourceAssignmentRequest)",
        "POST /api/resource-assignments/validate (ValidateResourceAssignmentRequest)",
        "POST /api/resource-assignments/validate-batch (ValidateResourceAssignmentBatchRequest)",
        "POST /api/resource-groups/{groupId:guid}/capabilities/ (AddGroupCapabilityRequest)",
        "POST /api/resources/ (CreateResourceRequest)",
        "POST /api/resources/{id:guid}/capabilities (AddResourceCapabilityRequest)",
        "POST /api/scheduling/auto-schedule/apply (AutoScheduleApplyRequest)",
        "POST /api/scheduling/auto-schedule/preview (AutoSchedulePreviewRequest)",
        "POST /api/session/tos/accept (TosAcceptRequest)",
        "POST /api/sites/{siteId:guid}/spaces/{resourceId:guid}/capabilities (AddResourceCapabilityRequest)",
        "POST /api/templates/ (CreateTemplateRequest)",
        "POST /api/templates/{id:guid}/items (CreateTemplateItemRequest)",
        "PUT /api/account/notification-preferences (UpdateNotificationPreferencesRequest)",
        "PUT /api/admin/announcements/{id:guid} (UpdateAnnouncementRequest)",
        "PUT /api/admin/configuration/ (UpdateSettingsRequest)",
        "PUT /api/admin/settings (UpdateSettingsRequest)",
        "PUT /api/criteria/{id:guid}/applicability (UpdateCriterionApplicabilityRequest)",
        "PUT /api/resource-groups/{id:guid}/members (SetResourceGroupMembersRequest)",
        "PUT /api/resources/{id:guid} (UpdateResourceRequest)",
        "PUT /api/settings/ (UpdateSettingsRequest)",
        "PUT /api/templates/{id:guid} (UpdateTemplateRequest)",
    };

    [Fact]
    public void EveryMutatingRoute_WithARequestModel_InjectsItsValidator()
    {
        var dataSource = _fixture.Factory.Services.GetRequiredService<EndpointDataSource>();

        var uncovered = new List<string>();
        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                          ?? (IReadOnlyList<string>)Array.Empty<string>();
            var isMutating = methods.Any(m =>
                HttpMethods.IsPost(m) || HttpMethods.IsPut(m) || HttpMethods.IsPatch(m) || HttpMethods.IsDelete(m));
            if (!isMutating) continue;

            var path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            if (!path.StartsWith("/api")) continue;

            var handler = endpoint.Metadata.GetMetadata<MethodInfo>();
            if (handler is null) continue;

            var parameters = handler.GetParameters();
            // Convention: request bodies are *Request records/classes.
            var requestParams = parameters
                .Select(p => p.ParameterType)
                .Where(t => t.IsClass && t.Name.EndsWith("Request", StringComparison.Ordinal))
                .ToList();
            if (requestParams.Count == 0) continue;

            var validatedTypes = parameters
                .Select(p => p.ParameterType)
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IValidator<>))
                .Select(t => t.GetGenericArguments()[0])
                .ToHashSet();

            foreach (var requestType in requestParams.Where(t => !validatedTypes.Contains(t)))
            {
                var row = $"{string.Join(",", methods)} {path} ({requestType.Name})";
                if (!UncoveredWhenIntroduced.Contains(row))
                    uncovered.Add(row);
            }
        }

        Assert.True(uncovered.Count == 0,
            "These mutating /api routes bind a *Request model without injecting IValidator<TRequest>. "
            + "Add a FluentValidation validator and inject it in the handler (see existing validators "
            + "in Api.Validators), or — only for routes that predate the ratchet — add the exact row "
            + "to UncoveredWhenIntroduced:\n  "
            + string.Join("\n  ", uncovered.OrderBy(x => x)));
    }
}
