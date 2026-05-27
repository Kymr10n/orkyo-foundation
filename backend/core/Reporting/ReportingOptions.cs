namespace Api.Reporting;

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    public bool Enabled { get; set; } = true;

    /// <summary>Internal base URL of Superset — used by the API to call Superset's REST API.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Browser-facing base URL of Superset — used in the embed URL returned to the frontend.
    /// Defaults to <see cref="BaseUrl"/> when not set (correct for host-mode dev and production
    /// where internal and public URLs are the same).
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Superset service-account username used to issue guest tokens.</summary>
    public string AdminUsername { get; set; } = "";

    /// <summary>Superset service-account password.</summary>
    public string AdminPassword { get; set; } = "";

    /// <summary>Lifetime of each issued guest token in seconds. Matches the SDK refresh window.</summary>
    public int EmbedTokenTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Master secret used to deterministically derive per-tenant rpt_reader passwords via HMAC-SHA256.
    /// Must be a non-empty string; rotate by incrementing <c>credentials_version</c> in
    /// <c>tenant_reporting_state</c> and re-provisioning.
    /// </summary>
    public string ReaderCredentialMasterSecret { get; set; } = "";

    /// <summary>
    /// Map of report key → template dashboard UUID in Superset.
    /// The provisioner uses these as the source to copy per-tenant dashboards from.
    /// Populated by the admin after creating template dashboards in Phase 4.
    /// </summary>
    public Dictionary<string, string> TemplateDashboardIds { get; set; } = new();

    /// <summary>
    /// Postgres host that Superset connects to when creating per-tenant datasources.
    /// Typically the internal hostname of the Postgres container.
    /// </summary>
    public string PostgresHost { get; set; } = "postgres";

    public int PostgresPort { get; set; } = 5432;
}
