namespace Api.Services.Transfer;

/// <summary>What a tenant transfer does with one tenant table.</summary>
public enum TransferTableClass
{
    /// <summary>Exported. An import erases the target rows and loads the archive rows.</summary>
    Travels,

    /// <summary>Exported in part, never erased: an import merges the archive rows into the target rows.</summary>
    Merged,

    /// <summary>Not exported, and an import leaves the target rows alone.</summary>
    Kept,

    /// <summary>Not exported, and an import erases the target rows.</summary>
    Erased,
}

/// <summary>One tenant table as the transfer sees it.</summary>
/// <param name="Name">The table name in the <c>public</c> schema.</param>
/// <param name="Class">What export and import do with the table.</param>
/// <param name="UserColumns">
/// Columns that hold a <c>users.id</c>, with or without a foreign key. An import rewrites them
/// to the id the user has on the target.
/// </param>
/// <param name="DeferredColumn">
/// A nullable column that closes a foreign-key cycle between two rows: a list definition
/// names one of its own columns. An import loads it as NULL and sets it once every table is
/// loaded.
/// </param>
/// <param name="ParentColumn">
/// A column that references the table's own primary key. The export writes parents before
/// children, so a load in archive order never meets a child whose parent is still to come.
/// </param>
/// <param name="LateColumn">
/// A nullable column that references <paramref name="LateAfter"/>, a table loaded after this
/// one. The cycle is between the tables, not between rows: the rows that leave the column
/// empty load at this table's place, and the rows that fill it load right after
/// <paramref name="LateAfter"/>. Used where a check constraint forbids loading the column
/// as NULL.
/// </param>
/// <param name="LateAfter">The table <paramref name="LateColumn"/> references.</param>
/// <param name="BlobColumn">
/// The table's binary column. Its values travel as bytes, one archive entry per row, and never
/// as text inside the row: a floorplan as text costs many times its size in memory.
/// </param>
/// <param name="EncryptedColumns">
/// Columns the application encrypts at rest under the tenant's id. The archive carries their
/// plain values: the export decrypts them, and the import encrypts them under the target.
/// No other column is ever read as an envelope, whatever its text looks like.
/// </param>
public sealed record TransferTable(
    string Name,
    TransferTableClass Class,
    IReadOnlyList<string> UserColumns,
    string? DeferredColumn = null,
    string? ParentColumn = null,
    string? LateColumn = null,
    string? LateAfter = null,
    string? BlobColumn = null,
    IReadOnlyList<string>? EncryptedColumns = null)
{
    public IReadOnlyList<string> EncryptedColumns { get; } = EncryptedColumns ?? [];
}

/// <summary>
/// The one classification of every tenant-phase table for a tenant transfer. The lock
/// statement, the erase order, the load order, the user-id rewrite and the re-encryption all come from this
/// list, so there is no second list to keep in step.
/// <para>
/// <see cref="All"/> is ordered parents-first: a table appears after every table it
/// references, the deferred and late columns aside. <c>TenantTransferTableConformanceTests</c> checks
/// the list against a freshly migrated tenant database, so a migration that adds a table,
/// a foreign key, a user column or a binary column fails the build until it is classified here.
/// </para>
/// </summary>
public static class TenantTransferTables
{
    public const string Users = "users";

    /// <summary>The table whose rows carry the id of the tenant that owns them.</summary>
    public const string Assets = "assets";

    public static readonly IReadOnlyList<TransferTable> All =
    [
        // The tenant's user mirror. In Community it is also the account table.
        new(Users, TransferTableClass.Merged, []),

        new("sites", TransferTableClass.Travels, [], EncryptedColumns: ["description", "address"]),
        new("resource_types", TransferTableClass.Travels, []),
        new("criteria", TransferTableClass.Travels, []),
        new("criterion_resource_types", TransferTableClass.Travels, []),
        new("resource_groups", TransferTableClass.Travels, []),
        new("resources", TransferTableClass.Travels, ["linked_user_id"], EncryptedColumns: ["notes"]),
        new("resource_group_members", TransferTableClass.Travels, []),
        new("resource_capabilities", TransferTableClass.Travels, []),
        new("resource_group_capabilities", TransferTableClass.Travels, []),
        // list_definitions <-> list_columns: a definition names its display column.
        new("list_definitions", TransferTableClass.Travels, [], DeferredColumn: "display_column_id"),
        new("list_columns", TransferTableClass.Travels, []),
        // list_instances <-> resource_custom_fields: a lookup field names a shared instance,
        // and a per-resource instance names the field it belongs to. Check constraints tie
        // both columns to the row's kind, so neither can be loaded empty: the shared
        // instances go in first, then the fields, then the per-resource instances.
        new("list_instances", TransferTableClass.Travels, [],
            LateColumn: "field_id", LateAfter: "resource_custom_fields"),
        new("resource_custom_fields", TransferTableClass.Travels, []),
        new("list_rows", TransferTableClass.Travels, []),
        new("availability_events", TransferTableClass.Travels, []),
        new("availability_event_scopes", TransferTableClass.Travels, []),
        new("resource_absences", TransferTableClass.Travels, []),
        // No foreign key on the user column, on purpose: a scan code outlives its creator.
        new("resource_scan_codes", TransferTableClass.Travels, ["created_by_user_id"]),
        new("templates", TransferTableClass.Travels, []),
        new("template_items", TransferTableClass.Travels, []),
        new("template_target_resource_types", TransferTableClass.Travels, []),
        new("routings", TransferTableClass.Travels, []),
        new("routing_steps", TransferTableClass.Travels, []),
        new("requests", TransferTableClass.Travels, [], ParentColumn: "parent_request_id"),
        new("request_requirements", TransferTableClass.Travels, []),
        new("request_target_resource_types", TransferTableClass.Travels, []),
        new("request_dependencies", TransferTableClass.Travels, []),
        new("resource_assignments", TransferTableClass.Travels, []),
        new("scheduling_settings", TransferTableClass.Travels, []),
        new("preset_applications", TransferTableClass.Travels, ["applied_by_user_id"]),
        new("preset_mappings", TransferTableClass.Travels, []),
        new(Assets, TransferTableClass.Travels, ["created_by_user_id", "updated_by_user_id"],
            BlobColumn: "data", EncryptedColumns: ["data"]),
        // Site roles. Nothing writes this table today; its rows travel with the sites they name.
        new("memberships", TransferTableClass.Travels, ["user_id"]),

        // Derived: the search triggers rebuild it while the tables load.
        new("search_documents", TransferTableClass.Erased, []),
        // Token hashes are bearer credentials: a feed URL of the source must not open the target.
        new("calendar_feed_tokens", TransferTableClass.Erased, []),

        // The migration journal. Its ids travel in the manifest as the schema version.
        new("orkyo_schema_migrations", TransferTableClass.Kept, []),
        // The target's audit history must survive an import.
        new("audit_events", TransferTableClass.Kept, []),
        // Preferences belong to the account on the target.
        new("user_preferences", TransferTableClass.Kept, []),
        // Security limits, branding and terms text. An import skips the settings validator.
        new("tenant_settings", TransferTableClass.Kept, []),
        // No AI table travels: the key is a secret, the usage is the target's accounting,
        // the conversations are private to their users, and the limits belong with the key.
        new("ai_credentials", TransferTableClass.Kept, []),
        new("ai_usage", TransferTableClass.Kept, []),
        new("ai_daily_usage", TransferTableClass.Kept, []),
        new("ai_conversations", TransferTableClass.Kept, []),
        new("ai_user_allowances", TransferTableClass.Kept, []),
        new("ai_daily_limits", TransferTableClass.Kept, []),
    ];

    /// <summary>The tables an archive carries beside <see cref="Users"/>, parents first.</summary>
    public static readonly IReadOnlyList<TransferTable> Travelling =
        All.Where(t => t.Class == TransferTableClass.Travels).ToList();

    /// <summary>
    /// The tables an import empties, children first: the erased tables, which may reference a
    /// travelling one, then the travelling tables in reverse load order.
    /// </summary>
    public static readonly IReadOnlyList<TransferTable> EraseOrder =
        All.Where(t => t.Class == TransferTableClass.Erased)
            .Concat(Travelling.Reverse())
            .ToList();
}
