namespace Api.Validators;

/// <summary>
/// Patterns validated in more than one place, so the rule and its wording live once.
///
/// All anchored with <c>\A</c> and <c>\z</c> rather than <c>^</c> and <c>$</c>, following
/// <see cref="ResourceTypeKeyRules"/>: in .NET <c>$</c> also matches immediately before a
/// trailing newline, so <c>"#ffffff\n"</c> passed the hex-colour check that every copy of
/// these patterns used. The stricter anchors are what the values actually mean.
/// </summary>
internal static class ValidationPatterns
{
    /// <summary>A six-digit hex colour, with the leading hash.</summary>
    public const string HexColor = @"\A#[0-9A-Fa-f]{6}\z";

    /// <summary>Kept verbatim from ResourceGroupRequestValidator.</summary>
    public const string HexColorMessage = "Color must be a valid hex color (#RRGGBB)";

    /// <summary>An identifier that starts with a letter: criterion names and the like.</summary>
    public const string Identifier = @"\A[a-zA-Z][a-zA-Z0-9_-]*\z";

    /// <summary>Kept verbatim from the criterion validators — user-facing copy, not a detail.</summary>
    public const string IdentifierMessage =
        "Name must start with a letter and contain only letters, numbers, underscores, and hyphens";
}
