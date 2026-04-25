namespace Api.Services;

/// <summary>
/// Floorplan local-storage path conventions shared by all products.
///
/// The on-disk layout is <c>{basePath}/tenant_{tenantId}/floorplans/{siteId}_{uniqueId}{extension}</c>
/// and the relative path stored in the database is the same minus <c>{basePath}</c>.
/// Both multi-tenant SaaS and single-tenant Community deployments use this layout
/// (in single-tenant mode there is exactly one <c>tenant_{tenantId}</c> directory).
/// </summary>
public static class FloorplanStoragePathPolicy
{
    /// <summary>Sub-directory under the per-tenant root that holds floorplan files.</summary>
    public const string FloorplanSubdirectory = "floorplans";

    /// <summary>Per-tenant directory prefix (e.g. <c>tenant_{guid}</c>).</summary>
    public const string TenantDirectoryPrefix = "tenant_";

    /// <summary>Build the relative path stored in the DB: <c>tenant_{tenantId}/floorplans/{fileName}</c>.</summary>
    public static string BuildRelativePath(Guid tenantId, string fileName)
        => Path.Combine($"{TenantDirectoryPrefix}{tenantId}", FloorplanSubdirectory, fileName);

    /// <summary>Build the per-tenant floorplans directory: <c>{basePath}/tenant_{tenantId}/floorplans</c>.</summary>
    public static string BuildTenantFloorplanDirectory(string basePath, Guid tenantId)
        => Path.Combine(basePath, $"{TenantDirectoryPrefix}{tenantId}", FloorplanSubdirectory);

    /// <summary>Build the canonical filename: <c>{siteId}_{uniqueId}{extension}</c>.</summary>
    public static string BuildFileName(Guid siteId, Guid uniqueId, string extension)
        => $"{siteId}_{uniqueId}{extension}";
}
