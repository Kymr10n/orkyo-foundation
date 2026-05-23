namespace Api.Services;

/// <summary>
/// Pure validation rules for floorplan upload requests, shared by all products.
///
/// Lives in <c>orkyo-foundation</c> because the upload validation contract
/// (file-size limit messaging, MIME allow-list parsing and enforcement) is
/// identical across multi-tenant SaaS and single-tenant Community deployments;
/// the only product-specific concern is where the limits come from
/// (<c>TenantSettings</c> in SaaS, deployment config in Community).
/// </summary>
public static class FloorplanUploadValidationPolicy
{
    /// <summary>1 MiB in bytes (used for size-limit messaging).</summary>
    public const long BytesPerMegabyte = 1024L * 1024L;

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the upload is empty.
    /// </summary>
    public static void AssertNonEmpty(long fileLengthBytes)
    {
        if (fileLengthBytes == 0)
            throw new ArgumentException("File is empty");
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the upload exceeds
    /// <paramref name="maxFileSizeMb"/>. The message is caller-safe and
    /// surfaces the configured limit in MB.
    /// </summary>
    public static void AssertWithinSizeLimit(long fileLengthBytes, int maxFileSizeMb)
    {
        var maxBytes = maxFileSizeMb * BytesPerMegabyte;
        if (fileLengthBytes > maxBytes)
            throw new ArgumentException($"File size exceeds maximum of {maxFileSizeMb}MB");
    }

    /// <summary>
    /// Parse a comma-separated MIME allow-list (e.g. <c>"image/png, image/jpeg"</c>)
    /// into an ordered list with empty entries removed and whitespace trimmed.
    /// </summary>
    public static IReadOnlyList<string> ParseAllowedMimeTypes(string allowedMimeTypesCsv)
    {
        if (string.IsNullOrWhiteSpace(allowedMimeTypesCsv))
            return Array.Empty<string>();

        return allowedMimeTypesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="detectedMimeType"/>
    /// is not present in the parsed allow-list. The message includes the detected
    /// type and the configured allow-list, matching the existing wire contract.
    /// </summary>
    public static void AssertMimeAllowed(string detectedMimeType, IReadOnlyCollection<string> allowedMimeTypes)
    {
        if (!allowedMimeTypes.Contains(detectedMimeType))
            throw new ArgumentException(
                $"Detected file type '{detectedMimeType}' is not allowed. Allowed types: {string.Join(", ", allowedMimeTypes)}");
    }
}
