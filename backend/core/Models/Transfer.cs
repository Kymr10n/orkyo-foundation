namespace Api.Models;

/// <summary>
/// The <c>manifest.json</c> of a tenant transfer archive (format version 1). It says where the
/// archive came from, which schema version its rows have, and what the tables must contain.
/// </summary>
public sealed record TransferManifest
{
    public const int CurrentFormatVersion = 1;

    public required int FormatVersion { get; init; }

    /// <summary>The deployment mode of the source, as its diagnostics report it. Informational.</summary>
    public string? SourceEdition { get; init; }

    /// <summary>The application version of the source. Informational: the gate reads <see cref="SchemaVersion"/>.</summary>
    public string? AppVersion { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    public required Guid SourceTenantId { get; init; }

    public required string SourceTenantSlug { get; init; }

    /// <summary>
    /// The tenant-phase foundation migration ids the source database had applied. An import
    /// accepts the archive only when this equals the target's own set.
    /// </summary>
    public required IReadOnlyList<string> SchemaVersion { get; init; }

    /// <summary>Rows per table, <c>users</c> included. An import that loads a different count fails.</summary>
    public required IReadOnlyDictionary<string, long> Tables { get; init; }
}

/// <summary>A reason an archive cannot be imported into this tenant.</summary>
/// <param name="Code">One of the <c>transfer_*</c> wire codes.</param>
public sealed record TransferBlocker(string Code, string Message);

/// <summary>What <c>POST /api/admin/transfer/import/validate</c> reports about an archive.</summary>
public sealed record TransferValidationResult
{
    public string? SourceEdition { get; init; }
    public string? AppVersion { get; init; }
    public required DateTimeOffset ExportedAt { get; init; }
    public required string SourceTenantSlug { get; init; }

    /// <summary>Empty when the archive can be imported.</summary>
    public required IReadOnlyList<TransferBlocker> Blockers { get; init; }

    public required IReadOnlyDictionary<string, long> Tables { get; init; }

    /// <summary>Archive users that map to a user of this tenant, by id or by email.</summary>
    public required int MatchedUsers { get; init; }

    /// <summary>
    /// Emails of archive users with no match here. An import turns each one into a permanent
    /// placeholder; inviting the person first makes the import map them instead.
    /// </summary>
    public required IReadOnlyList<string> UnmatchedEmails { get; init; }
}

/// <summary>What a completed import reports.</summary>
public sealed record TransferImportResult
{
    /// <summary>Rows loaded across all tables.</summary>
    public required long Rows { get; init; }

    public required int MatchedUsers { get; init; }

    public required int PlaceholderUsers { get; init; }
}

/// <summary>
/// Where a running import is. <see cref="Percent"/> is set for the load only, the one step
/// with something to measure: rows loaded of the rows the manifest announces.
/// </summary>
public sealed record TransferProgress(string Step, int? Percent = null)
{
    public const string Erase = "erase";
    public const string Load = "load";
    public const string Finish = "finish";
}
