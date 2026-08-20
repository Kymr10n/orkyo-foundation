namespace Api.Constants;

/// <summary>
/// The historical seed keys from migration 1300. No type is built in any more — a fresh
/// database has no types until the tenant activates catalog entries or creates their own —
/// so these constants carry no runtime meaning. They remain for the two places that name
/// them: the space-first tiebreak in PresetApplier, and the test fixtures that create the
/// classic three types for themselves.
/// </summary>
public static class ResourceTypeKeys
{
    public const string Space = "space";
    public const string Person = "person";
    public const string Tool = "tool";
}
