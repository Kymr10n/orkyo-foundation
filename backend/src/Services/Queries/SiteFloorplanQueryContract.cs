namespace Api.Services;

/// <summary>
/// SQL contract for writing floorplan metadata onto a tenant-database
/// <c>sites</c> row. Structurally identical in multi-tenant SaaS and
/// single-tenant Community: both deployments host the per-tenant <c>sites</c>
/// table with the same <c>floorplan_*</c> column set, and the upload-side
/// projection (<c>FloorplanMetadata</c>) is already shared via the foundation
/// read path (<c>SiteRepository</c>/<c>SiteMapper</c>).
/// </summary>
public static class SiteFloorplanQueryContract
{
    public const string ImagePathParameterName = "path";
    public const string MimeTypeParameterName = "mime";
    public const string FileSizeBytesParameterName = "size";
    public const string WidthPxParameterName = "w";
    public const string HeightPxParameterName = "h";
    public const string SiteIdParameterName = "siteId";

    /// <summary>
    /// UPDATE that writes the full floorplan metadata block onto a single
    /// <c>sites</c> row, stamping <c>floorplan_uploaded_at = NOW()</c>.
    /// </summary>
    public static string BuildUpdateFloorplanSql()
    {
        return @"
                UPDATE sites SET
                    floorplan_image_path = @path,
                    floorplan_mime_type = @mime,
                    floorplan_file_size_bytes = @size,
                    floorplan_width_px = @w,
                    floorplan_height_px = @h,
                    floorplan_uploaded_at = NOW()
                WHERE id = @siteId
            ";
    }
}
