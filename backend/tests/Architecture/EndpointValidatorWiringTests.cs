using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// The wiring half of validation coverage, complementing
/// <see cref="RequestValidatorCoverageTests"/>. That guard asks whether an
/// <see cref="AbstractValidator{T}"/> <b>exists</b> for each <c>*Request</c> type; this one asks
/// whether an existing validator is actually <b>injected</b> at the mutating endpoint that binds
/// the type. A validator that is written but never wired is invisible to the type-level guard and
/// silently enforces nothing.
///
/// <para>Deliberately allowlist-free. A request type with no validator at all is
/// <see cref="RequestValidatorCoverageTests"/>'s business — it owns that baseline and its
/// justifications — so this test simply says nothing about those types rather than duplicating
/// the list. The two guards share their reflection through
/// <see cref="RequestValidationReflection"/>.</para>
/// </summary>
[Collection("Database collection")]
public class EndpointValidatorWiringTests
{
    private readonly DatabaseFixture _fixture;

    public EndpointValidatorWiringTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void EveryMutatingRoute_WithAValidatedRequestType_InjectsThatValidator()
    {
        var validatedTypes = RequestValidationReflection.ValidatedRequestTypes();
        Assert.NotEmpty(validatedTypes); // vacuity guard: no validators found means the scan broke

        var dataSource = _fixture.Factory.Services.GetRequiredService<EndpointDataSource>();

        var unwired = new List<string>();
        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                          ?? (IReadOnlyList<string>)Array.Empty<string>();
            if (!methods.Any(m => HttpMethods.IsPost(m) || HttpMethods.IsPut(m)
                                  || HttpMethods.IsPatch(m) || HttpMethods.IsDelete(m)))
                continue;

            var path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            if (!path.StartsWith("/api", StringComparison.Ordinal)) continue;

            var handler = endpoint.Metadata.GetMetadata<MethodInfo>();
            if (handler is null) continue;

            var parameters = handler.GetParameters().Select(p => p.ParameterType).ToList();

            // Only types that HAVE a validator are in scope here (see the class remarks).
            var boundRequestTypes = parameters
                .Where(t => t.IsClass && t.Name.EndsWith("Request", StringComparison.Ordinal))
                .Where(validatedTypes.Contains)
                .ToList();
            if (boundRequestTypes.Count == 0) continue;

            var injected = parameters
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IValidator<>))
                .Select(t => t.GetGenericArguments()[0])
                .ToHashSet();

            foreach (var requestType in boundRequestTypes.Where(t => !injected.Contains(t)))
                unwired.Add($"{string.Join(",", methods)} {path} ({requestType.Name})");
        }

        var offenders = unwired.Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.True(offenders.Count == 0,
            "These mutating /api routes bind a *Request type that HAS an AbstractValidator<T>, but "
            + "the handler never injects IValidator<T> — so the validator exists and enforces "
            + "nothing. Inject it and apply it via EndpointHelpers.ExecuteAsync:\n  "
            + string.Join("\n  ", offenders));
    }
}
