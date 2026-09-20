using System.Reflection;
using Api.Repositories;
using Api.Services;
using Xunit;

namespace Orkyo.Foundation.Tests.Architecture;

/// <summary>
/// Self-guarding data-isolation conformance test. Every concrete repository must declare its data
/// scope through one of the two sanctioned dependencies:
///
///   • <see cref="OrgContext"/>            → tenant-scoped (queries the caller's tenant DB via
///                                            <c>IOrgDbConnectionFactory.CreateOrgConnection(orgContext)</c>),
///   • <see cref="IOrgContextAccessor"/>   → tenant-scoped, for a repository that must construct in a scope
///                                            with no tenant (it yields only the caller's own OrgContext or
///                                            null, and the repository throws on null), or
///   • <see cref="IDbConnectionFactory"/>  → control-plane (an explicit, reviewed cross-tenant choice).
///
/// A repository that takes neither has no sanctioned way to obtain a scoped connection, so it would
/// have to build its own — the exact path by which a future change (human or AI) could silently read
/// the wrong tenant's (or every tenant's) data. Adding such a repository fails this test. See
/// docs/validation.md and the tenant-isolation notes in the architecture review.
/// </summary>
public class RepositoryScopingTests
{
    [Fact]
    public void EveryConcreteRepository_DeclaresItsDataScope()
    {
        var repositories = typeof(ResourceRepository).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Repository", StringComparison.Ordinal) || TakesAConnectionDependency(t))
            .ToList();

        // Sanity: the reflection query must actually find the repositories — a zero-count pass
        // would render the guard useless if the assembly/namespace ever moves.
        Assert.NotEmpty(repositories);

        var offenders = repositories
            .Where(t => !DeclaresDataScope(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These repositories take none of OrgContext / IOrgContextAccessor (tenant-scoped) or " +
            "IDbConnectionFactory (control-plane) in any constructor, so their data scope is undeclared and could leak " +
            "across tenants. Add the appropriate dependency:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Anything that takes a connection factory or an <see cref="OrgContext"/> reads the database
    /// the way a repository does, whatever it is called — the ~18 services that still carry their
    /// own SQL included. Selecting on the dependency rather than the <c>*Repository</c> suffix
    /// keeps the guard on a query that moves into a differently-named type.
    /// </summary>
    private static bool TakesAConnectionDependency(Type type) =>
        type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(OrgContext)
                   || p.ParameterType == typeof(IDbConnectionFactory));

    private static bool DeclaresDataScope(Type repository) =>
        repository.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(OrgContext)
                   || p.ParameterType == typeof(IOrgContextAccessor)
                   || p.ParameterType == typeof(IDbConnectionFactory));
}
