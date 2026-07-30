namespace Api.Constants;

/// <summary>
/// Data types a custom resource-type field may declare. Values are stored in
/// <c>resources.metadata_json</c> and validated against the field definition.
/// </summary>
public static class ResourceFieldDataTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string Select = "select";

    /// <summary>Known data types, in canonical order.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Text,
        Number,
        Boolean,
        Date,
        Select,
    };

    public static bool IsKnown(string? dataType) => dataType is not null && All.Contains(dataType);
}
