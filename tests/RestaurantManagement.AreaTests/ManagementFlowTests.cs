using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

internal static class ManagementFlowTests
{
    internal static async Task Run(HttpClient client, string connection)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS: Management flow " + label); }
        async Task<string> Holidays()
        {
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT Id,HolidayDate,Name,IsActive FROM dbo.SpecialHolidays ORDER BY Id FOR JSON PATH", cn);
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        var before = await Holidays();
        Check(before != "[]", "existing holidays available for preservation test");
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/OpeningHours"));
        Check(page.Contains("holiday-panel") && page.Contains("calendar-panel") && page.Contains("DefaultBookingMinutes") && page.Contains("Thử đặt bàn"), "one screen contains all configuration and trial links");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var data = new Dictionary<string,string> { ["__RequestVerificationToken"] = token, ["DefaultBookingMinutes"] = "150" };
        for (var i = 0; i < 7; i++)
        {
            data[$"Days[{i}].DayOfWeek"] = (i + 1).ToString();
            data[$"Days[{i}].OpensAt"] = "08:15";
            data[$"Days[{i}].ClosesAt"] = "22:00";
            data[$"Days[{i}].IsClosed"] = (i == 6).ToString();
        }
        using (var response = await client.PostAsync("/OpeningHours", new FormUrlEncodedContent(data)))
            Check(response.StatusCode == HttpStatusCode.Redirect, "save changed week and duration");
        Check(await Holidays() == before, "weekly save preserves all holiday IDs dates names and states");
        page = WebUtility.HtmlDecode(await client.GetStringAsync("/OpeningHours"));
        Check(page.Contains("value=\"150\"") && page.Contains("value=\"08:15\"") && page.Contains("Ngày nghỉ — không nhận đặt bàn") && page.Contains("Lễ kiểm thử"), "reload shows duration hours closed day and holidays together");
        data["__RequestVerificationToken"] = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        data["Days[0].ClosesAt"] = "07:00";
        using (var response = await client.PostAsync("/OpeningHours", new FormUrlEncodedContent(data)))
        {
            var invalid = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Check(response.StatusCode == HttpStatusCode.OK && invalid.Contains("data-valmsg-for=\"Days[0].ClosesAt\"") && invalid.Contains("Giờ đóng cửa phải lớn hơn giờ mở cửa") && invalid.Contains("Lễ kiểm thử"), "invalid form displays field error and retains holiday panel");
        }
        Check(await Holidays() == before, "invalid save preserves holidays");
    }
}
