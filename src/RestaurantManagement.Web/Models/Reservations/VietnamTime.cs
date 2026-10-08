using System.Globalization;
using System.Text.RegularExpressions;

namespace RestaurantManagement.Web.Models.Reservations;

public static class VietnamTime
{
    public const string ZoneId = "Asia/Ho_Chi_Minh";
    public static TimeZoneInfo Zone { get; } = FindZone();
    public static DateTime Now => FromUtc(DateTime.UtcNow);
    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    public static bool TryParseBooking(string? value, out DateTime local)
    {
        local = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Offset-free ISO input is Vietnam wall time, never the server/device time zone.
        var match = Regex.Match(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,3})?)?(?<offset>Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        try
        {
            if (match.Groups["offset"].Success)
            {
                if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)) return false;
                local = FromUtc(instant.UtcDateTime);
            }
            else
            {
                if (!DateTime.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFF"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out local)) return false;
                local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            }
            // Leave room for UTC conversion and the configured reservation duration in SQL.
            return local.Year is >= 1900 and <= 9998;
        }
        catch (ArgumentException) { return false; }
    }

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(ZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}
