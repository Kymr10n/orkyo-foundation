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
        // --- pure paging / single-field payloads: no cross-field or format invariant exists ---
        "Api.Models.PageRequest",                              // page/pageSize only
        "Api.Models.Reporting.ReportingPageRequest",           // page/pageSize only
        "Api.Endpoints.TosAcceptRequest",                      // single accept flag
        "Api.Endpoints.UpdateNotificationPreferencesRequest",  // single bool opt-out flag
        "Api.Models.UpdateRequestRequirementRequest",          // single JsonElement; shape is the
                                                               // criterion datatype's business

        // --- invariants owned elsewhere: a static validator would duplicate the real policy ---
        // Server-constructed from the multipart form in FloorplanEndpoints (never model-bound),
        // and its rules are settings-driven — size cap and MIME allowlist come from tenant
        // settings via FloorplanUploadValidationPolicy, which no static validator can express.
        "Api.Models.UploadFloorplanRequest",
    };

    [Fact]
    public void EveryRequestType_HasValidatorOrIsAllowlisted()
    {
        var requestTypes = RequestValidationReflection.RequestTypes().ToList();

        // Sanity: a zero-count scan would make the guard vacuous if the assemblies/namespaces move.
        Assert.NotEmpty(requestTypes);

        var validated = RequestValidationReflection.ValidatedRequestTypes();

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
        var requestFullNames = RequestValidationReflection.RequestTypes()
            .Select(t => t.FullName!)
            .ToHashSet(StringComparer.Ordinal);
        var validatedFullNames = RequestValidationReflection.ValidatedRequestTypes()
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

}
