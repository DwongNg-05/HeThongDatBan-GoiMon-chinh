using RestaurantManagement.Web.Services;

internal static class KitchenTimingTests
{
    internal static void Run(Action<bool, string> check)
    {
        var sent = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        var start = sent.AddMinutes(3);
        KitchenLine Line(string status, DateTimeOffset? began, DateTimeOffset? end, DateTimeOffset now) =>
            new(1, 1, "A01", "Canh", 1, null, status, "version", sent, began, end, now);
        var pending = Line("Pending", null, null, start);
        check(pending.ActualCookingMilliseconds is null && pending.WaitingMilliseconds == 180000, "Kitchen timing: pending has only waiting time");
        var preparing = Line("Preparing", start, null, start.AddSeconds(5));
        check(preparing.ActualCookingMilliseconds is null && preparing.ElapsedCookingMilliseconds == 5000, "Kitchen timing: preparing has elapsed time, no actual duration");
        check(Line("Preparing", start, null, start.AddSeconds(6)).ElapsedCookingMilliseconds > preparing.ElapsedCookingMilliseconds,
            "Kitchen timing: elapsed duration grows with server time");
        check(Line("Ready", start, start.AddMilliseconds(1), start.AddSeconds(9)).ActualCookingMilliseconds == 1,
            "Kitchen timing: rapid transition preserves millisecond precision");
        check(Line("Ready", start, start.AddMinutes(90), start.AddHours(3)).ActualCookingMilliseconds == 5400000,
            "Kitchen timing: slow dish duration stops at ready, excludes waiting time");
        check(Line("Ready", null, null, start).ActualCookingMilliseconds is null,
            "Kitchen timing: old dishes without timestamps do not invent a duration");
        check(Line("Ready", start.ToOffset(TimeSpan.FromHours(7)), start.AddSeconds(30), start).ActualCookingMilliseconds == 30000,
            "Kitchen timing: duration is independent of display timezone");
    }
}
