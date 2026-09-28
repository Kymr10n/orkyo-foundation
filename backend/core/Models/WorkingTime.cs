using System.Globalization;

namespace Api.Models;

/// <summary>
/// A working-day boundary as the settings carry it: exactly <c>HH:mm</c>. <c>TimeSpan.TryParse</c>
/// read "5" as five days, which then failed the <c>time</c> column write with a 500.
/// </summary>
internal static class WorkingTime
{
    public const string Format = "HH:mm";

    public static bool TryParse(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>For a value the validator already accepted.</summary>
    public static TimeOnly Parse(string value) =>
        TimeOnly.ParseExact(value, Format, CultureInfo.InvariantCulture);
}
