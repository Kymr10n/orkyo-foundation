using FluentValidation;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Shared reflection over the request/validator surface, used by the two complementary guards:
/// <see cref="RequestValidatorCoverageTests"/> (does a validator exist for each <c>*Request</c>
/// type?) and <see cref="EndpointValidatorWiringTests"/> (is an existing validator actually
/// injected at the endpoint that binds the type?).
/// </summary>
internal static class RequestValidationReflection
{
    // *Request DTOs live in the Core assembly (Api.Models + endpoint-adjacent records) and the Web
    // assembly (records declared alongside their endpoints). Anchor one known type in each to force
    // both assemblies loaded before reflecting over them.
    private static readonly System.Reflection.Assembly[] Assemblies =
    [
        typeof(Api.Validators.ContactRequestValidator).Assembly, // Orkyo.Foundation.Core
        typeof(Api.Endpoints.SecurityEndpoints).Assembly,        // Orkyo.Foundation.Web
    ];

    /// <summary>Every public, concrete <c>*Request</c> type crossing the API boundary.</summary>
    public static IEnumerable<Type> RequestTypes() =>
        Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.IsVisible)
            .Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal))
            .Distinct();

    /// <summary>Every T for which a closed <see cref="AbstractValidator{T}"/> subclass exists.</summary>
    public static HashSet<Type> ValidatedRequestTypes()
    {
        var validated = new HashSet<Type>();
        foreach (var assembly in Assemblies)
            foreach (var type in assembly.GetTypes())
            {
                if (type is not { IsClass: true, IsAbstract: false }) continue;

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
