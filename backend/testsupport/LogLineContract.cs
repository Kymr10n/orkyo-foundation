using System.Text.RegularExpressions;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// A log line names the matched route template, never the raw request path: a secret carried
/// in the path (the anonymous calendar feed token) would land in the log (foundation#277).
/// Log through <c>HttpContext.LoggablePath()</c>. Foundation's convention ratchet and the
/// products' architecture tests share this one definition of the offence.
/// </summary>
public static partial class LogLineContract
{
    /// <summary>A log call that passes <c>Request.Path</c>. Spans lines; stops at the statement's <c>;</c>.</summary>
    [GeneratedRegex(@"\.Log\w*\([^;]*\bRequest\.Path\b")]
    public static partial Regex RawRequestPathInLog();

    /// <summary>
    /// The <c>root/relative</c> path of every <c>*.cs</c> file under <paramref name="roots"/>
    /// (children of <paramref name="backendRoot"/>, <c>bin</c> and <c>obj</c> skipped) that offends.
    /// </summary>
    public static IReadOnlyList<string> RawRequestPathOffenders(string backendRoot, params string[] roots) =>
        roots
            .SelectMany(root => Directory.EnumerateFiles(Path.Combine(backendRoot, root), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => RawRequestPathInLog().IsMatch(File.ReadAllText(f)))
                .Select(f => Path.GetRelativePath(backendRoot, f).Replace('\\', '/')))
            .Order()
            .ToList();
}
