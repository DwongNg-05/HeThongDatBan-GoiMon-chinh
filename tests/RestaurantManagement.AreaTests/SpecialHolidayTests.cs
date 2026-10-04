using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

internal static class SpecialHolidayTests
{
    internal static async Task Run(HttpClient client, string connection)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS: Holidays " + label); }
        async Task<string> Get(string path) => WebUtility.HtmlDecode(await client.GetStringAsync(path));
        async Task<HttpResponseMessage> Post(string form, string action, Dictionary<string,string> values)
        {
            var html = await Get(form);
            values["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            return await client.PostAsync(action, new FormUrlEncodedContent(values));
        }
        Dictionary<string,string> Values(string date, string name = "Ngày lễ thử nghiệm", bool active = true) => new()
        { ["HolidayDate"] = date, ["Name"] = name, ["IsActive"] = active.ToString() };
        async Task<int> Scalar(string sql)
        {
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn); return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        const string first = "2030-01-07", second = "2030-01-08"; // Monday and Tuesday.
        Check((await Get("/OpeningHours/Calendar?date=" + first)).Contains("daily-booking-slots"), "weekly working day has slots");
        using (var response = await Post("/SpecialHolidays/Create", "/SpecialHolidays/Create", Values(first)))
            Check(response.StatusCode == HttpStatusCode.Redirect, "create succeeds");
        var id = await Scalar("SELECT Id FROM dbo.SpecialHolidays WHERE HolidayDate='20300107'");
        Check((await Get("/SpecialHolidays")).Contains("Ngày lễ thử nghiệm"), "saved holiday listed");
        var calendar = await Get("/OpeningHours/Calendar?date=" + first);
        Check(calendar.Contains("Ngày lễ thử nghiệm") && !calendar.Contains("daily-booking-slots"), "holiday overrides open weekly day");
        Dictionary<string,string> Booking() => new()
        {
            ["CustomerName"] = "Khách kiểm thử ngày nghỉ", ["Phone"] = "0987654321",
            ["GuestCount"] = "2", ["StartsAt"] = first + "T11:00"
        };
        var beforeBooking = await Scalar("SELECT COUNT(*) FROM dbo.Reservations");
        using (var response = await Post("/Reservations/Create", "/Reservations/Create", Booking()))
            Check(response.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("Ngày bạn chọn là ngày nghỉ đặc biệt"), "actual booking rejected with holiday message");
        Check(await Scalar("SELECT COUNT(*) FROM dbo.Reservations") == beforeBooking, "blocked booking creates no reservation");
        await using (var cn = new SqlConnection(connection))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("EXEC dbo.usp_CreateReservation @CustomerName=N'Direct holiday test',@Phone='0987654322',@GuestCount=2,@StartsAt='2030-01-07T04:00:00'", cn);
            var rejected = false;
            try { await cmd.ExecuteNonQueryAsync(); }
            catch (SqlException ex) when (ex.Number == 51410) { rejected = true; }
            Check(rejected, "direct database booking cannot bypass holiday guard (Vietnam date)");
        }
        using (var response = await Post("/SpecialHolidays/Create", "/SpecialHolidays/Create", Values(first)))
            Check(response.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("Ngày nghỉ này đã tồn tại"), "duplicate date rejected");
        Check(await Scalar("SELECT COUNT(*) FROM dbo.SpecialHolidays") == 1, "duplicate does not insert");
        using (var response = await Post($"/SpecialHolidays/Edit/{id}", $"/SpecialHolidays/Edit/{id}", Values(first, "Tạm ngừng lễ", false)))
            Check(response.StatusCode == HttpStatusCode.Redirect, "disable holiday");
        Check((await Get("/OpeningHours/Calendar?date=" + first)).Contains("daily-booking-slots"), "inactive holiday restores weekly slots");
        using (var response = await Post("/Reservations/Create", "/Reservations/Create", Booking()))
            Check(response.StatusCode == HttpStatusCode.Redirect, "actual booking allowed when holiday disabled");
        Check(await Scalar("SELECT COUNT(*) FROM dbo.Reservations") == beforeBooking + 1, "allowed booking persisted");
        using (var response = await Post("/SpecialHolidays/Create", "/SpecialHolidays/Create", Values(first)))
            Check(response.StatusCode == HttpStatusCode.OK && await Scalar("SELECT COUNT(*) FROM dbo.SpecialHolidays") == 1, "inactive date still unique");
        using (var response = await Post($"/SpecialHolidays/Edit/{id}", $"/SpecialHolidays/Edit/{id}", Values(second, "Lễ đã đổi")))
            Check(response.StatusCode == HttpStatusCode.Redirect, "edit date name and state");
        Check((await Get("/OpeningHours/Calendar?date=" + first)).Contains("daily-booking-slots") && !(await Get("/OpeningHours/Calendar?date=" + second)).Contains("daily-booking-slots"), "moving holiday restores old day and blocks new day");
        using (var response = await Post("/SpecialHolidays/Create", "/SpecialHolidays/Create", Values(first)))
            Check(response.StatusCode == HttpStatusCode.Redirect, "create second holiday");
        using (var response = await Post($"/SpecialHolidays/Edit/{id}", $"/SpecialHolidays/Edit/{id}", Values(first)))
            Check(response.StatusCode == HttpStatusCode.OK && await Scalar($"SELECT COUNT(*) FROM dbo.SpecialHolidays WHERE Id={id} AND HolidayDate='20300108'") == 1, "duplicate edit preserves original date");
        foreach (var values in new[] { Values(""), Values("invalid"), Values(first, "   "), Values(first, new string('x',151)) })
        {
            using var response = await Post("/SpecialHolidays/Create", "/SpecialHolidays/Create", values);
            Check(response.StatusCode == HttpStatusCode.OK && await Scalar("SELECT COUNT(*) FROM dbo.SpecialHolidays") == 2, "invalid holiday not saved");
        }
        await Get($"/SpecialHolidays/Delete/{id}");
        Check(await Scalar($"SELECT COUNT(*) FROM dbo.SpecialHolidays WHERE Id={id}") == 1, "delete GET does not mutate");
        using (var response = await client.PostAsync($"/SpecialHolidays/Delete/{id}", new FormUrlEncodedContent(new Dictionary<string,string>())))
            Check(response.StatusCode == HttpStatusCode.BadRequest, "delete requires antiforgery token");
        using (var response = await Post($"/SpecialHolidays/Delete/{id}", $"/SpecialHolidays/Delete/{id}", new()))
            Check(response.StatusCode == HttpStatusCode.Redirect, "delete succeeds");
        Check(await Scalar($"SELECT COUNT(*) FROM dbo.SpecialHolidays WHERE Id={id}") == 0 && (await Get("/OpeningHours/Calendar?date=" + second)).Contains("daily-booking-slots"), "delete restores weekly calendar");
        using var missing = await client.GetAsync($"/SpecialHolidays/Edit/{id}");
        Check(missing.StatusCode == HttpStatusCode.NotFound, "missing holiday returns 404");
    }
}
