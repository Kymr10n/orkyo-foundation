using System.Text.Json;
using Api.Repositories;

namespace Api.Services.Transfer;

/// <summary>A user as an archive carries it: identity only, no credentials and no lifecycle state.</summary>
public sealed record TransferArchiveUser(Guid Id, string Email, string? DisplayName, DateTimeOffset CreatedAt)
{
    /// <summary>The archive writes PostgreSQL's column names.</summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}

/// <summary>How an archive's users land on a target.</summary>
/// <param name="IdMap">Archive user id to the id that user has on the target.</param>
/// <param name="Matched">Archive users that are an existing target user.</param>
/// <param name="Placeholders">Archive users with no match, which an import inserts under their own id.</param>
public sealed record TransferUserMatch(
    IReadOnlyDictionary<Guid, Guid> IdMap, int Matched, IReadOnlyList<TransferArchiveUser> Placeholders);

/// <summary>
/// Maps the users of an archive onto the users of the target tenant. The target's users are
/// never erased: accounts and memberships live outside the tenant data, so the people who can
/// sign in after an import are the people who could before it.
/// </summary>
public static class TransferUserMatcher
{
    /// <summary>
    /// Id first, then email. The id match is what makes an import into the installation the
    /// archive came from work after someone changed their email: the row is the same person,
    /// and a match by email alone would try to insert a second user under an id that exists.
    /// </summary>
    public static TransferUserMatch Match(
        IReadOnlyCollection<TransferArchiveUser> archiveUsers, IReadOnlyCollection<TransferTargetUser> targetUsers)
    {
        var targetIds = targetUsers.Select(u => u.Id).ToHashSet();
        var targetByEmail = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in targetUsers)
            targetByEmail[user.Email] = user.Id;

        var idMap = new Dictionary<Guid, Guid>();
        var placeholders = new List<TransferArchiveUser>();
        foreach (var user in archiveUsers)
        {
            if (idMap.ContainsKey(user.Id)) continue;
            if (targetIds.Contains(user.Id))
                idMap[user.Id] = user.Id;
            else if (targetByEmail.TryGetValue(user.Email, out var targetId))
                idMap[user.Id] = targetId;
            else
            {
                idMap[user.Id] = user.Id;
                placeholders.Add(user);
            }
        }
        return new TransferUserMatch(idMap, idMap.Count - placeholders.Count, placeholders);
    }
}
