namespace Api.Helpers;

/// <summary>
/// Generates deterministic logical keys from display names.
/// Used by preset and export systems for portable, ID-free references.
/// </summary>
public static class KeyHelpers
{
    public static string GenerateKey(string name)
    {
        return name
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-")
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .Aggregate("", (current, c) => current + c)
            .Trim('-');
    }
}
