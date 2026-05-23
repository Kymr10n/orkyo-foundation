namespace Api.Helpers;

/// <summary>
/// Pure display helpers for diagnostics endpoints. Structurally identical in
/// multi-tenant SaaS and single-tenant Community deployments.
/// </summary>
public static class DiagnosticsDisplayHelpers
{
    /// <summary>
    /// Masks a hostname for safe display in diagnostics responses by replacing
    /// every middle label with <c>*****</c>. Hosts with two or fewer labels
    /// (e.g. <c>localhost</c>, <c>example.com</c>) are returned unchanged
    /// because there is nothing to mask without dropping the TLD or leaf.
    /// </summary>
    /// <example>
    /// <c>smtp.mail.example.com</c> → <c>smtp.*****.*****.com</c><br/>
    /// <c>smtp.example.com</c> → <c>smtp.*****.com</c><br/>
    /// <c>example.com</c> → <c>example.com</c> (no change)
    /// </example>
    public static string MaskHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return host;

        var parts = host.Split('.');
        if (parts.Length <= 2) return host;
        for (var i = 1; i < parts.Length - 1; i++)
            parts[i] = "*****";
        return string.Join('.', parts);
    }
}
