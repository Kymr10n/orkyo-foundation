using Api.Models.Account;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Assembles <see cref="PersonalDataExport"/> for the signed-in person: their control-plane rows,
/// then their rows in every organization database they belong to. The queries live in
/// <see cref="PersonalDataExportRepository"/>, which mirrors <see cref="UserPurgeRepository"/>.
/// </summary>
public interface IPersonalDataExportService
{
    /// <summary>Null when no user row exists for <paramref name="userId"/>.</summary>
    Task<PersonalDataExport?> ExportAsync(Guid userId, CancellationToken ct = default);
}

public sealed class PersonalDataExportService : IPersonalDataExportService
{
    public const string SchemaVersion = "1.0";

    private readonly PersonalDataExportRepository _repository;
    private readonly IPlatformUserRepository _users;
    private readonly TimeProvider _time;

    public PersonalDataExportService(IDbConnectionFactory connectionFactory, IPlatformUserRepository users, TimeProvider time)
    {
        _repository = new PersonalDataExportRepository(connectionFactory);
        _users = users;
        _time = time;
    }

    public async Task<PersonalDataExport?> ExportAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _repository.GetProfileAsync(userId, ct);
        if (profile is null)
            return null;

        var databases = await _repository.ListOrganizationDatabasesAsync(userId, ct);
        var organizations = new List<PersonalOrganizationData>(databases.Count);
        foreach (var (db, slug, name) in databases)
        {
            ct.ThrowIfCancellationRequested();
            organizations.Add(await _repository.GetOrganizationDataAsync(db, slug, name, userId, ct));
        }

        return new PersonalDataExport
        {
            SchemaVersion = SchemaVersion,
            ExportedAt = _time.GetUtcNow().UtcDateTime,
            Profile = profile,
            Identities = await _users.GetIdentitiesAsync(userId, ct),
            TermsAcceptances = await _repository.GetTermsAcceptancesAsync(userId, ct),
            Sessions = await _repository.GetSessionsAsync(userId, ct),
            Memberships = await _users.GetMembershipsAsync(userId, ct),
            Feedback = await _repository.GetFeedbackAsync(userId, ct),
            Organizations = organizations,
        };
    }
}
