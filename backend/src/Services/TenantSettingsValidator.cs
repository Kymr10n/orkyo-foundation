using System.Globalization;
using System.Text.RegularExpressions;
using Api.Models;

namespace Api.Services;

/// <summary>
/// Pure validation policy for tenant-setting values against their descriptors.
///
/// Throws <see cref="ArgumentException"/> when a value violates the descriptor's
/// type, range, or format constraints. Universal length cap matches the underlying
/// TEXT column safety limit.
/// </summary>
public static class TenantSettingsValidator
{
    /// <summary>Universal length cap for any setting value (TEXT column safety limit).</summary>
    public const int MaxStringLength = 500;

    private static readonly Regex _hexColorPattern = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Regex _mimeTypePattern = new(@"^[a-z]+/[a-z0-9\.\-\+]+$", RegexOptions.Compiled);
    private static readonly Regex _htmlTagPattern = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// Validate <paramref name="value"/> against <paramref name="descriptor"/>.
    /// Throws <see cref="ArgumentException"/> on any violation.
    /// </summary>
    public static void Validate(TenantSettingDescriptor descriptor, string value)
    {
        if (value.Length > MaxStringLength)
            throw new ArgumentException($"Setting '{descriptor.Key}' value exceeds maximum length of {MaxStringLength} characters");

        switch (descriptor.ValueType)
        {
            case "int":
                if (!int.TryParse(value, CultureInfo.InvariantCulture, out var intVal))
                    throw new ArgumentException($"Setting '{descriptor.Key}' must be an integer");
                if (descriptor.MinValue != null && intVal < int.Parse(descriptor.MinValue, CultureInfo.InvariantCulture))
                    throw new ArgumentException($"Setting '{descriptor.Key}' minimum is {descriptor.MinValue}");
                if (descriptor.MaxValue != null && intVal > int.Parse(descriptor.MaxValue, CultureInfo.InvariantCulture))
                    throw new ArgumentException($"Setting '{descriptor.Key}' maximum is {descriptor.MaxValue}");
                break;

            case "double":
                if (!double.TryParse(value, CultureInfo.InvariantCulture, out var dblVal))
                    throw new ArgumentException($"Setting '{descriptor.Key}' must be a number");
                if (descriptor.MinValue != null && dblVal < double.Parse(descriptor.MinValue, CultureInfo.InvariantCulture))
                    throw new ArgumentException($"Setting '{descriptor.Key}' minimum is {descriptor.MinValue}");
                if (descriptor.MaxValue != null && dblVal > double.Parse(descriptor.MaxValue, CultureInfo.InvariantCulture))
                    throw new ArgumentException($"Setting '{descriptor.Key}' maximum is {descriptor.MaxValue}");
                break;

            case "bool":
                if (!bool.TryParse(value, out _))
                    throw new ArgumentException($"Setting '{descriptor.Key}' must be 'true' or 'false'");
                break;

            case "string":
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException($"Setting '{descriptor.Key}' cannot be empty");
                ValidateStringFormat(descriptor, value);
                break;
        }
    }

    private static void ValidateStringFormat(TenantSettingDescriptor descriptor, string value)
    {
        // Hex color fields
        if (descriptor.Key.EndsWith("_color", StringComparison.OrdinalIgnoreCase))
        {
            if (!_hexColorPattern.IsMatch(value))
                throw new ArgumentException($"Setting '{descriptor.Key}' must be a valid hex color (e.g. #ff0000)");
        }

        // MIME type list (comma-separated)
        if (descriptor.Key.Contains("mime_types", StringComparison.OrdinalIgnoreCase))
        {
            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                throw new ArgumentException($"Setting '{descriptor.Key}' must contain at least one MIME type");
            foreach (var part in parts)
            {
                if (!_mimeTypePattern.IsMatch(part))
                    throw new ArgumentException($"Setting '{descriptor.Key}' contains invalid MIME type: '{part}'");
            }
        }

        // Product name — no HTML/script tags, max 100 chars
        if (descriptor.Key.Contains("product_name", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length > 100)
                throw new ArgumentException($"Setting '{descriptor.Key}' must not exceed 100 characters");
            if (_htmlTagPattern.IsMatch(value))
                throw new ArgumentException($"Setting '{descriptor.Key}' must not contain HTML tags");
        }
    }
}
