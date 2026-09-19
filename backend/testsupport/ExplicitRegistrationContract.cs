using System.Text.RegularExpressions;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// The explicit-registration rule, shared so each product checks its own <c>Program.cs</c>
/// with the same classification: if <c>Program.cs</c> calls <c>app.UseX()</c>, it must also call
/// a matching <c>AddX()</c> in the same file, never relying on <c>AddFoundationServices</c> to
/// register a service the product uses directly. Foundation is a NuGet package consumed in
/// CI/Docker, so an implicit dependency on it registering a service breaks silently during the
/// publish window.
/// </summary>
/// <remarks>
/// The <see cref="UseToAdd"/> map is a self-ratchet: any <c>UseX</c> that is neither mapped nor
/// in <see cref="NoRegistrationNeeded"/> nor in the caller's transitional exceptions is reported
/// as unclassified, forcing whoever adds new middleware to classify it. Products pass their
/// edition-specific rows to <see cref="Check"/>; the helper returns findings instead of
/// asserting, so it carries no test-framework dependency.
/// </remarks>
public static class ExplicitRegistrationContract
{
    /// <summary>
    /// Middleware activation -> the DI registration(s) that satisfy it (any one is enough), for
    /// middleware every edition mounts. Entries for middleware a product does not use are simply
    /// never exercised.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> UseToAdd = new Dictionary<string, string[]>
    {
        ["UseAuthentication"] = ["AddOrkyoAuthentication", "AddAuthentication"],
        ["UseAuthorization"] = ["AddAuthorization"],
        ["UseCors"] = ["AddOrkyoApiCors", "AddCors"],
        // Community registers the limiter via AddFoundationRateLimiting, SaaS via AddOrkyoRateLimiting.
        ["UseRateLimiter"] = ["AddFoundationRateLimiting", "AddOrkyoRateLimiting", "AddRateLimiter"],
        ["UseAuthenticatedRateLimiting"] = ["AddOrkyoRateLimiting"],
        ["UseFoundationMiddleware"] = ["AddFoundationServices"],
        // Composed shared pipeline — everything it mounts is registered by
        // AddFoundationServices (auth, CSRF, context enrichment).
        ["UseOrkyoPipeline"] = ["AddFoundationServices"],
        ["UseResponseCompression"] = ["AddResponseCompression"],
        ["UseOrkyoReportingSwaggerUI"] = ["AddOrkyoReportingSwagger"],
    };

    /// <summary>Middleware with no same-file DI registration requirement.</summary>
    public static readonly IReadOnlySet<string> NoRegistrationNeeded = new HashSet<string>(StringComparer.Ordinal)
    {
        "UseRouting",
        "UseHttpsRedirection",
        // Foundation's opt-in Prometheus helper (wraps prometheus-net UseHttpMetrics);
        // the registry is process-wide static state — there is nothing to register.
        "UseOrkyoMetrics",
    };

    private static readonly Regex UseCallRegex = new(@"\bapp\.(Use[A-Za-z0-9]+)\(", RegexOptions.Compiled);

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> (typically <c>AppContext.BaseDirectory</c>)
    /// to the checkout root and returns <c>backend/api/Program.cs</c>, or null when not found.
    /// </summary>
    public static string? FindProgramCs(string startDirectory)
    {
        var dir = startDirectory;
        for (var i = 0; i < 12; i++)
        {
            var candidate = Path.Combine(dir, "backend", "api", "Program.cs");
            if (File.Exists(candidate)) return candidate;

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null) break;
            dir = parent;
        }
        return null;
    }

    /// <summary>
    /// Classifies every <c>app.UseX()</c> call in <paramref name="programContent"/>.
    /// </summary>
    /// <param name="programContent">The text of the product's <c>Program.cs</c>.</param>
    /// <param name="extraUseToAdd">Edition-specific rows; a key present here replaces the shared row.</param>
    /// <param name="extraNoRegistrationNeeded">Edition-specific middleware that needs no registration.</param>
    /// <param name="knownTransitionalExceptions">Documented, tracked deviations the edition still carries.</param>
    public static ExplicitRegistrationFindings Check(
        string programContent,
        IReadOnlyDictionary<string, string[]>? extraUseToAdd = null,
        IEnumerable<string>? extraNoRegistrationNeeded = null,
        IEnumerable<string>? knownTransitionalExceptions = null)
    {
        ArgumentNullException.ThrowIfNull(programContent);

        var useToAdd = new Dictionary<string, string[]>(UseToAdd, StringComparer.Ordinal);
        foreach (var (use, adds) in extraUseToAdd ?? new Dictionary<string, string[]>())
            useToAdd[use] = adds;

        var noRegistration = new HashSet<string>(NoRegistrationNeeded, StringComparer.Ordinal);
        noRegistration.UnionWith(extraNoRegistrationNeeded ?? []);
        var transitional = new HashSet<string>(knownTransitionalExceptions ?? [], StringComparer.Ordinal);

        var uses = UseCallRegex.Matches(programContent)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();

        var unclassified = uses
            .Where(u => !useToAdd.ContainsKey(u) && !noRegistration.Contains(u) && !transitional.Contains(u))
            .ToList();

        var missing = uses
            .Where(useToAdd.ContainsKey)
            .Where(u => !useToAdd[u].Any(add => Regex.IsMatch(programContent, $@"\.{Regex.Escape(add)}\s*\(")))
            .Select(u => $"{u} (expected one of: {string.Join(", ", useToAdd[u])})")
            .ToList();

        return new ExplicitRegistrationFindings(uses, unclassified, missing);
    }
}

/// <summary>Result of <see cref="ExplicitRegistrationContract.Check"/>; the caller asserts on it.</summary>
/// <param name="Uses">Every distinct <c>UseX</c> found, sorted.</param>
/// <param name="Unclassified">Activations that appear in no map or list.</param>
/// <param name="Missing">Activations whose <c>AddX()</c> is absent from the same file.</param>
public sealed record ExplicitRegistrationFindings(
    IReadOnlyList<string> Uses,
    IReadOnlyList<string> Unclassified,
    IReadOnlyList<string> Missing)
{
    /// <summary>Assertion message for <see cref="Unclassified"/>, the same in every host.</summary>
    public string ExplainUnclassified() =>
        "these middleware activations are unclassified — add each to UseToAdd (with its AddX), "
        + "NoRegistrationNeeded, or KnownTransitionalExceptions in ExplicitRegistrationTests:\n  "
        + string.Join("\n  ", Unclassified);

    /// <summary>Assertion message for <see cref="Missing"/>, the same in every host.</summary>
    public string ExplainMissing() =>
        "these middleware are activated but their AddX() is not called in the same Program.cs "
        + "(explicit-registration rule):\n  " + string.Join("\n  ", Missing);
}
