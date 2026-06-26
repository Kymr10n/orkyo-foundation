namespace Api.Security;

/// <summary>
/// Minimal, dependency-free User-Agent parser. Extracts the coarse
/// browser family, OS family, and device type needed for the account
/// "Active Sessions" list — not analytics-grade precision.
///
/// Deliberately dependency-free (no license surface). If finer parsing is ever
/// needed, UAParser (.NET, Apache-2.0, OSS / free for commercial use) is the
/// approved upgrade path.
/// </summary>
public static class UserAgentParser
{
    public const string DeviceDesktop = "desktop";
    public const string DeviceMobile = "mobile";
    public const string DeviceTablet = "tablet";
    public const string DeviceUnknown = "unknown";

    public readonly record struct ParsedUserAgent(string? Browser, string? OperatingSystem, string DeviceType);

    public static ParsedUserAgent Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return new ParsedUserAgent(null, null, DeviceUnknown);

        var ua = userAgent;

        return new ParsedUserAgent(
            Browser: DetectBrowser(ua),
            OperatingSystem: DetectOs(ua),
            DeviceType: DetectDeviceType(ua));
    }

    private static string? DetectBrowser(string ua)
    {
        // Order matters: Edge/Opera/Chrome all also contain "Chrome"/"Safari".
        if (Contains(ua, "Edg")) return "Edge";
        if (Contains(ua, "OPR") || Contains(ua, "Opera")) return "Opera";
        if (Contains(ua, "SamsungBrowser")) return "Samsung Internet";
        if (Contains(ua, "Firefox") || Contains(ua, "FxiOS")) return "Firefox";
        if (Contains(ua, "Chrome") || Contains(ua, "CriOS")) return "Chrome";
        if (Contains(ua, "Safari")) return "Safari";
        return null;
    }

    private static string? DetectOs(string ua)
    {
        // iOS / iPadOS before generic Mac; Android before Linux.
        if (Contains(ua, "iPhone") || Contains(ua, "iPad") || Contains(ua, "iPod")) return "iOS";
        if (Contains(ua, "Android")) return "Android";
        if (Contains(ua, "Windows")) return "Windows";
        if (Contains(ua, "Mac OS X") || Contains(ua, "Macintosh")) return "macOS";
        if (Contains(ua, "CrOS")) return "ChromeOS";
        if (Contains(ua, "Linux")) return "Linux";
        return null;
    }

    private static string DetectDeviceType(string ua)
    {
        if (Contains(ua, "iPad") || Contains(ua, "Tablet")) return DeviceTablet;
        // Android without "Mobile" is conventionally a tablet.
        if (Contains(ua, "Android") && !Contains(ua, "Mobile")) return DeviceTablet;
        if (Contains(ua, "Mobile") || Contains(ua, "iPhone") || Contains(ua, "iPod") || Contains(ua, "Android"))
            return DeviceMobile;
        return DeviceDesktop;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
