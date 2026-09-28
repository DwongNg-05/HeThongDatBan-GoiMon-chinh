using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

internal static class BookingScheduleTests
{
    internal static async Task Run(HttpClient client, string connection)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS: Schedule " + label); }
        async Task Execute(string sql)
        {
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn); await cmd.ExecuteNonQueryAsync();
        }
        async Task<int> Count()
        {
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.Reservations", cn);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        await Execute("UPDATE dbo.OpeningHours SET IsClosed=0,OpensAt='08:00',ClosesAt='22:00'; UPDATE dbo.RestaurantSettings SET DefaultBookingMinutes=90 WHERE Id=1;");
        var serial = 0;
        async Task Post(string local, bool allowed, string message = "")
        {
            var preview = await client.GetStringAsync("/Reservations/CheckSchedule?startsAt=" + Uri.EscapeDataString(local));
            using var json = JsonDocument.Parse(preview);
            Check(json.RootElement.GetProperty("allowed").GetBoolean() == allowed, "instant check " + local);
            if (!allowed) Check(json.RootElement.GetProperty("message").GetString()!.Contains(message), "specific preview reason");
            var form = await client.GetStringAsync("/Reservations/Create");
            var token = WebUtility.HtmlDecode(Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var before = await Count();
            using var response = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["__RequestVerificationToken"] = token, ["CustomerName"] = "Khách kiểm thử lịch",
                ["Phone"] = "097" + (++serial).ToString("D7"), ["GuestCount"] = "2", ["StartsAt"] = local
            }));
            Check(response.StatusCode == (allowed ? HttpStatusCode.Redirect : HttpStatusCode.OK), "submission outcome " + local);
            if (!allowed) Check(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains(message), "submission reason");
            Check(await Count() == before + (allowed ? 1 : 0), "only valid requests are persisted");
        }
        const string monday = "2031-01-06";
        await Post(monday + "T08:00", true);
        await Post(monday + "T12:30", true);
        await Post(monday + "T21:30", true); // Ends after closing, allowed by the agreed arrival rule.
        await Post(monday + "T07:30", false, "trước giờ đóng cửa");
        await Post(monday + "T22:00", false, "trước giờ đóng cửa");
        await Post(monday + "T22:30", false, "trước giờ đóng cửa");
        await Post(monday + "T08:15", false, "30 phút");
        await Post(monday + "T08:00:01", false, "30 phút");
        await Execute("UPDATE dbo.OpeningHours SET IsClosed=1 WHERE DayOfWeek=1;");
        await Post(monday + "T12:00", false, "ngày nghỉ trong tuần");
        await Execute("INSERT dbo.SpecialHolidays(HolidayDate,Name,IsActive) VALUES('20310106',N'Lễ kiểm thử',1);");
        await Post(monday + "T12:00", false, "ngày nghỉ đặc biệt");
        await Execute("UPDATE dbo.OpeningHours SET IsClosed=0 WHERE DayOfWeek=1;");
        await Post(monday + "T12:00", false, "ngày nghỉ đặc biệt");
        await Execute("UPDATE dbo.SpecialHolidays SET IsActive=0 WHERE HolidayDate='20310106'; UPDATE dbo.OpeningHours SET OpensAt='06:15',ClosesAt='09:20' WHERE DayOfWeek=1;");
        await Post(monday + "T06:15", true);
        await Post(monday + "T06:45", true);
        await Post(monday + "T09:15", true);
        await Post(monday + "T09:20", false, "trước giờ đóng cửa");
        await Post(monday + "T08:00", false, "30 phút");
        // A valid preview must not authorize a later submission after a schedule change.
        var stale = await client.GetStringAsync("/Reservations/CheckSchedule?startsAt=" + monday + "T06:15");
        Check(stale.Contains("\"allowed\":true"), "preview initially allowed");
        await Execute("UPDATE dbo.OpeningHours SET IsClosed=1 WHERE DayOfWeek=1;");
        await Post(monday + "T06:15", false, "ngày nghỉ trong tuần");
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        await using var direct = new SqlCommand("EXEC dbo.usp_CreateReservation @CustomerName=N'Direct',@Phone='0979999999',@GuestCount=2,@StartsAt='2031-01-05T23:15:00'", cn);
        var rejected = false;
        try { await direct.ExecuteNonQueryAsync(); } catch (SqlException ex) when (ex.Number == 51411) { rejected = true; }
        Check(rejected, "direct SQL enforces weekly holiday using local date across UTC midnight");
    }
}
