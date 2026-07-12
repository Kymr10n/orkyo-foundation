using FluentValidation;
using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Self-guarding shape-validation conformance test, mirroring <see cref="RepositoryScopingTests"/>.
///
/// Every public <c>*Request</c> DTO (the objects that carry untrusted client input across the API
/// boundary) must declare its shape invariants through a closed <see cref="AbstractValidator{T}"/>,
/// applied via <c>EndpointHelpers.ExecuteAsync(request, validator, handler)</c>. A request type with
/// no validator forces handlers to improvise ad-hoc guards — the exact drift Wave 3.4 of the
/// optimization program exists to remove. The only sanctioned escape is an explicit
/// <see cref="NoShapeValidationNeeded"/> entry with a justifying comment (pure paging/query filters,
/// single-flag toggles — nothing with a cross-field or format invariant).
///
/// This is a <b>ratchet</b>: adding a new <c>*Request</c> type fails the test until it either gets a
/// validator or a justified allowlist entry, and an allowlist entry that later gains a validator must
/// be removed. The baseline is today's transitional state; Wave 3.4 shrinks the allowlist. See
/// docs/optimization-plan-2026-07.md (G2b, W3.4).
/// </summary>
public class RequestValidatorCoverageTests
{
    // Request DTOs that legitimately need no AbstractValidator<T>, keyed by full type name.
    // Each entry must name a real, currently-unvalidated *Request type; the second test below
    // fails if an entry goes stale (renamed away, or gained a validator). Wave 3.4 shrinks this.
    private static readonly HashSet<string> NoShapeValidationNeeded = new(StringComparer.Ordinal)
    {
        // --- genuinely shape-less: pure paging / single-flag toggles (expected to stay) ---
        "Api.Models.PageRequest",                       // page/pageSize only
        "Api.Models.Reporting.ReportingPageRequest",    // page/pageSize only
        "Api.Endpoints.TosAcceptRequest",               // single accept flag

        // --- transitional baseline: carry real invariants, want a validator. Wave 3.4 (W3.4)
        //     adds AbstractValidator<T> for these and removes the entry here as each lands. ---
        "Api.Endpoints.AddGroupCapabilityRequest",
        "Api.Endpoints.AddResourceCapabilityRequest",
        "Api.Endpoints.Admin.UpdateSettingsRequest",
        "Api.Endpoints.Reporting.CreateReportingTokenRequest",
        "Api.Endpoints.UpdateNotificationPreferencesRequest",
        "Api.Endpoints.UpdateSettingsRequest",
        "Api.Models.AddRequirementRequest",
        "Api.Models.AutoScheduleApplyRequest",
        "Api.Models.AutoSchedulePreviewRequest",
        "Api.Models.CreateAnnouncementRequest",
        "Api.Models.CreateRequestRequirementRequest",
        "Api.Models.CreateResourceAssignmentRequest",
        "Api.Models.CreateResourceRequest",
        "Api.Models.CreateTemplateItemRequest",
        "Api.Models.CreateTemplateRequest",
        "Api.Models.Export.ExportRequest",
        "Api.Models.LinkUserToPersonProfileRequest",
        "Api.Models.SetResourceGroupMembersRequest",
        "Api.Models.UpdateAnnouncementRequest",
        "Api.Models.UpdateCriterionApplicabilityRequest",
        "Api.Models.UpdateFeedbackRequest",
        "Api.Models.UpdateRequestRequirementRequest",
        "Api.Models.UpdateResourceRequest",
        "Api.Models.UpdateTemplateRequest",
        "Api.Models.UploadFloorplanRequest",
        "Api.Models.UpsertResourceCapabilityRequest",
        "Api.Models.ValidateResourceAssignmentBatchRequest",
        "Api.Models.ValidateResourceAssignmentRequest",
    };

    [Fact]
    public void EveryRequestType_HasValidatorOrIsAllowlisted()
    {
        var requestTypes = RequestTypes().ToList();

        // Sanity: a zero-count scan would make the guard vacuous if the assemblies/namespaces move.
        Assert.NotEmpty(requestTypes);

        var validated = ValidatedRequestTypes();

        var offenders = requestTypes
            .Where(t => !validated.Contains(t))
            .Select(t => t.FullName!)
            .Where(name => !NoShapeValidationNeeded.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These *Request types cross the API boundary with no AbstractValidator<T> and are not " +
            "allowlisted. Add a validator (applied via EndpointHelpers.ExecuteAsync) or, if the type " +
            "carries no shape invariant, add it to NoShapeValidationNeeded with a justifying comment:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Allowlist_HasNoStaleEntries()
    {
        var requestFullNames = RequestTypes()
            .Select(t => t.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        var validatedFullNames = ValidatedRequestTypes()
            .Select(t => t.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        var phantom = NoShapeValidationNeeded
            .Where(name => !requestFullNames.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var nowValidated = NoShapeValidationNeeded
            .Where(name => validatedFullNames.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            phantom.Count == 0,
            "These NoShapeValidationNeeded entries no longer name a *Request type (renamed or " +
            "removed) — delete them:\n  " + string.Join("\n  ", phantom));
        Assert.True(
            nowValidated.Count == 0,
            "These NoShapeValidationNeeded entries now have a validator — remove them so coverage " +
            "ratchets forward and can't silently regress:\n  " + string.Join("\n  ", nowValidated));
    }

    // *Request DTOs live in the Core assembly (Api.Models + endpoint-adjacent records) and the Web
    // assembly (records declared alongside their endpoints). Anchor one known type in each to force
    // both assemblies loaded before reflecting over them.
    private static IEnumerable<Type> RequestTypes() =>
        new[]
            {
                typeof(Api.Validators.ContactRequestValidator).Assembly, // Orkyo.Foundation.Core
                typeof(Api.Endpoints.SecurityEndpoints).Assembly,        // Orkyo.Foundation.Web
            }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.IsVisible)
            .Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal))
            .Distinct();

    // Every T for which a closed AbstractValidator<T> subclass exists. Validators live in both
    // the Core assembly (Api.Models request types) and the Web assembly (records declared
    // alongside their endpoints), mirroring RequestTypes() above.
    private static HashSet<Type> ValidatedRequestTypes()
    {
        var validated = new HashSet<Type>();
        var assemblies = new[]
        {
            typeof(Api.Validators.ContactRequestValidator).Assembly, // Orkyo.Foundation.Core
            typeof(Api.Endpoints.SecurityEndpoints).Assembly,        // Orkyo.Foundation.Web
        };
        foreach (var assembly in assemblies)
            foreach (var type in assembly.GetTypes())
            {
                if (type is not { IsClass: true, IsAbstract: false })
                {
                    continue;
                }

                for (var b = type.BaseType; b is not null; b = b.BaseType)
                {
                    if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                    {
                        validated.Add(b.GetGenericArguments()[0]);
                        break;
                    }
                }
            }

        return validated;
    }
}
