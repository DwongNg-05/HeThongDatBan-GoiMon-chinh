using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;

internal static class VietnamTimeTests
{
    internal static async Task Run(HttpClient client, string connection)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS: Vietnam time " + label); }
        foreach (var input in new[] { "2031-01-07T08:00", "2031-01-07T01:00Z", "2031-01-06T20:00-05:00", "2031-01-07T10:00+09:00", "2031-01-07T08:00+07:00" })
            Check(VietnamTime.TryParseBooking(input, out var local) && local == new DateTime(2031,1,7,8,0,0), "equivalent instant " + input);
        foreach (var input in new[] { "", "2031-01-07", "2031-02-30T08:00", "2031-01-07T08:00+25:00", "01/07/2031 08:00", "2031-01-07T08:00:00.0001" })
            Check(!VietnamTime.TryParseBooking(input, out _), "invalid or ambiguous input rejected");
        async Task Execute(string sql)
        {
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn); await cmd.ExecuteNonQueryAsync();
        }
        async Task CheckPreview(string time, bool allowed, string? reason = null)
        {
            using var json = JsonDocument.Parse(await client.GetStringAsync("/Reservations/CheckSchedule?startsAt=" + Uri.EscapeDataString(time)));
            Check(json.RootElement.GetProperty("allowed").GetBoolean() == allowed && (reason is null || json.RootElement.GetProperty("message").GetString()!.Contains(reason)), "preview " + time);
        }
        await Execute("UPDATE dbo.OpeningHours SET IsClosed=0,OpensAt='08:00',ClosesAt='22:00'; UPDATE dbo.SpecialHolidays SET IsActive=0 WHERE HolidayDate IN ('20310106','20310107');");
        foreach (var input in new[] { "2031-01-07T08:00", "2031-01-07T01:00Z", "2031-01-06T20:00-05:00", "2031-01-07T10:00+09:00" }) await CheckPreview(input, true);
        await CheckPreview("2031-01-07T00:30Z", false, "giờ mở cửa");
        await CheckPreview("2031-01-07T15:00Z", false, "trước giờ đóng cửa");
        var html = await client.GetStringAsync("/Reservations/Create");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        using (var response = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = token, ["CustomerName"] = "Timezone test", ["Phone"] = "0961234567",
            ["GuestCount"] = "2", ["StartsAt"] = "2031-01-06T20:00-05:00"
        }))) Check(response.StatusCode == HttpStatusCode.Redirect, "offset-bearing POST succeeds");
        await using (var cn = new SqlConnection(connection))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT Id,StartsAt FROM dbo.Reservations WHERE Phone='0961234567'", cn);
            await using var reader = await cmd.ExecuteReaderAsync();
            Check(await reader.ReadAsync() && reader.GetDateTime(1) == new DateTime(2031,1,7,1,0,0), "stored as UTC exactly once");
            var detail = WebUtility.HtmlDecode(await client.GetStringAsync("/Reservations/Details/" + reader.GetInt64(0)));
            Check(detail.Contains("07/01/2031 08:00"), "readback displays Vietnam time");
        }
        await Execute("UPDATE dbo.OpeningHours SET OpensAt='00:00',ClosesAt='23:59'; UPDATE dbo.OpeningHours SET IsClosed=1 WHERE DayOfWeek=7;");
        await CheckPreview("2031-01-05T17:00Z", true); // Monday 00:00 VN, Sunday UTC.
        await CheckPreview("2031-01-06T00:00+07:00", true);
        await CheckPreview("2031-01-05T16:30Z", false, "ngày nghỉ trong tuần");
        await Execute("UPDATE dbo.SpecialHolidays SET IsActive=1 WHERE HolidayDate='20310106';");
        await CheckPreview("2031-01-05T17:00Z", false, "ngày nghỉ đặc biệt");
        await CheckPreview("2031-01-05T12:00-05:00", false, "ngày nghỉ đặc biệt");
        await CheckPreview("2031-01-06T16:30Z", false, "ngày nghỉ đặc biệt");
        await CheckPreview("2031-01-06T17:00Z", true); // Tuesday 00:00 VN, Monday UTC.
        html = await client.GetStringAsync("/Reservations/Create");
        token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        using var denied = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["__RequestVerificationToken"] = token, ["CustomerName"] = "Timezone holiday", ["Phone"] = "0961234568",
            ["GuestCount"] = "2", ["StartsAt"] = "2031-01-05T17:00Z"
        }));
        var deniedHtml = WebUtility.HtmlDecode(await denied.Content.ReadAsStringAsync());
        Check(denied.StatusCode == HttpStatusCode.OK && deniedHtml.Contains("ngày nghỉ đặc biệt") && deniedHtml.Contains("2031-01-06T00:00"), "POST holiday uses Vietnam date and redisplays normalized local time");
    }
}
