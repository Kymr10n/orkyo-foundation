namespace Api.Constants;

/// <summary>
/// Entity-type discriminator stored in <c>search_documents.entity_type</c> and
/// switched on by <c>SearchRepository.GetOpenRoute</c> to build deep-link routes.
/// Independent of <see cref="ResourceTypeKeys"/> and <see cref="TemplateEntityTypes"/>;
/// overlapping values are coincidental, not shared ownership.
/// </summary>
public static class SearchEntityTypes
{
    public const string Space = "space";
    public const string Request = "request";
    public const string Group = "group";
    public const string Site = "site";
    public const string Template = "template";
    public const string Criterion = "criterion";
    public const string Person = "person";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Space,
        Request,
        Group,
        Site,
        Template,
        Criterion,
        Person,
    };

    public static bool IsKnown(string? entityType) =>
        entityType is not null && All.Contains(entityType);
}
