namespace RestaurantManagement.Web.Models.Reservations;

public static class BookingTime
{
    // Vietnam local time. SQL remains authoritative for opening hours and special dates.
    public static DateTime NextStart(DateTime now)
    {
        var next = now.Date.AddMinutes((Math.Floor(now.TimeOfDay.TotalMinutes / 30) + 1) * 30);
        if (next.TimeOfDay < TimeSpan.FromHours(8)) return next.Date.AddHours(8);
        if (next.TimeOfDay > new TimeSpan(21, 30, 0)) return next.Date.AddDays(1).AddHours(8);
        return next;
    }
}
