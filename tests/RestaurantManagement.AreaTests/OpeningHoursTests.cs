using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;

internal static class OpeningHoursTests
{
    internal static void Run(Action<bool, string> check)
    {
        var model = new OpeningHoursViewModel { Days = Enumerable.Range(1, 7).Select(day => new OpeningDayViewModel
            { DayOfWeek = day, OpensAt = "08:00", ClosesAt = "22:00" }).ToList() };
        bool Valid() => Validator.TryValidateObject(model, new ValidationContext(model), [], true);
        check(Valid(), "Opening hours: valid week");
        check(model.DefaultBookingMinutes == 90, "Booking duration defaults to 90 minutes");
        var slots = model.Days[0].BookingSlots;
        check(slots.Count == 28 && slots[0] == "08:00" && slots[^1] == "21:30", "08:00–22:00 includes 28 slots ending 21:30 regardless of duration");
        check(slots.Zip(slots.Skip(1)).All(pair => TimeOnly.Parse(pair.Second) - TimeOnly.Parse(pair.First) == TimeSpan.FromMinutes(30)), "Every slot is 30 minutes apart");
        var offsetDay = new OpeningDayViewModel { OpensAt = "08:15", ClosesAt = "09:20" };
        check(offsetDay.BookingSlots.SequenceEqual(new[] { "08:15", "08:45", "09:15" }), "Slots start at exact opening time with uneven close");
        offsetDay.OpensAt = "23:40"; offsetDay.ClosesAt = "23:59";
        check(offsetDay.BookingSlots.SequenceEqual(new[] { "23:40" }), "Short late interval does not wrap midnight");
        offsetDay.IsClosed = true;
        check(offsetDay.BookingSlots.Count == 0, "Closed days have no slots");
        model.DefaultBookingMinutes = 120;
        check(Valid() && model.Days[0].BookingSlots.SequenceEqual(slots), "Changing duration leaves arrival slots unchanged");
        foreach (var invalid in new int?[] { null, 0, 29, 361 })
        {
            model.DefaultBookingMinutes = invalid;
            check(!Valid(), "Invalid duration rejected: " + invalid);
        }
        model.DefaultBookingMinutes = 90;
        model.Days[0].ClosesAt = "08:00";
        check(!Valid(), "Opening hours: equal times rejected");
        model.Days[0].ClosesAt = "07:00";
        check(!Valid(), "Opening hours: reversed times rejected");
        model.Days[0].ClosesAt = "25:00";
        check(!Valid(), "Opening hours: malformed time rejected");
        model.Days[0].IsClosed = true;
        model.Days[0].OpensAt = model.Days[0].ClosesAt = null;
        check(Valid(), "Opening hours: closed day needs no time");
        model.Days[0].DayOfWeek = 2;
        check(!Valid(), "Opening hours: duplicate day rejected");
        model.Days.RemoveAt(0);
        check(!Valid(), "Opening hours: incomplete week rejected");
    }

    internal static async Task RunHttp(HttpClient client, string connection)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS: Opening hours " + label); }
        async Task<string> Snapshot()
        {
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using var command = new SqlCommand("SELECT DayOfWeek,IsClosed,OpensAt,ClosesAt,(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1) AS Duration FROM dbo.OpeningHours ORDER BY DayOfWeek FOR JSON PATH, INCLUDE_NULL_VALUES", cn);
            return (string)(await command.ExecuteScalarAsync())!;
        }
        async Task<HttpResponseMessage> Post(string close, bool closed = false, bool duplicate = false, string duration = "90")
        {
            var html = await client.GetStringAsync("/OpeningHours");
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var values = new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) };
            values["DefaultBookingMinutes"] = duration;
            for (var i = 0; i < 7; i++)
            {
                values[$"Days[{i}].DayOfWeek"] = (duplicate && i == 0 ? 2 : i + 1).ToString();
                values[$"Days[{i}].OpensAt"] = closed && i == 6 ? "" : "09:00";
                values[$"Days[{i}].ClosesAt"] = closed && i == 6 ? "" : i == 0 ? close : "21:00";
                values[$"Days[{i}].IsClosed"] = (closed && i == 6).ToString();
            }
            return await client.PostAsync("/OpeningHours", new FormUrlEncodedContent(values));
        }
        Check((await Snapshot()).Contains("\"Duration\":90"), "database duration defaults to 90");
        using (var response = await Post("22:00")) Check(response.StatusCode == HttpStatusCode.Redirect, "valid save redirects");
        Check((await Snapshot()).Contains("22:00:00"), "valid hours persisted");
        using (var response = await Post("22:00", true)) Check(response.StatusCode == HttpStatusCode.Redirect, "closed day saved");
        var saved = await Snapshot();
        Check(saved.Contains("\"IsClosed\":true,\"OpensAt\":null,\"ClosesAt\":null"), "closed day persisted without hours");
        var reopened = await client.GetStringAsync("/OpeningHours");
        Check(reopened.Contains("value=\"22:00\"") && Regex.IsMatch(reopened, "<input[^>]*checked=\"checked\"[^>]*name=\"Days\\[6\\].IsClosed\""), "reopening loads saved week");
        foreach (var close in new[] { "09:00", "08:00", "25:00", "" })
        {
            using var response = await Post(close);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Check(response.StatusCode == HttpStatusCode.OK && html.Contains(close is "09:00" or "08:00" ? "Giờ đóng cửa phải lớn hơn giờ mở cửa." : "Vui lòng nhập giờ đóng cửa hợp lệ"), "invalid time shows error: " + close);
            Check(await Snapshot() == saved, "invalid POST preserves entire week");
        }
        using (var response = await Post("22:00", duplicate: true))
            Check(response.StatusCode == HttpStatusCode.OK && await Snapshot() == saved, "duplicate day cannot overwrite week");
        using (var response = await Post("22:00", true, duration: "120"))
            Check(response.StatusCode == HttpStatusCode.Redirect && (await Snapshot()).Contains("\"Duration\":120"), "changed duration persisted with week");
        var changed = WebUtility.HtmlDecode(await client.GetStringAsync("/OpeningHours"));
        Check(changed.Contains("value=\"120\"") && changed.Contains(">21:30</span>") && !changed.Contains(">22:00</span>"), "reopen displays changed duration and exclusive closing boundary");
        var closedRow = Regex.Match(changed, "<tr>\\s*<th scope=\"row\">Chủ nhật[\\s\\S]*?</tr>").Value;
        Check(closedRow.Length > 0 && !closedRow.Contains("badge bg-light"), "closed day renders no slots");
        saved = await Snapshot();
        foreach (var invalidDuration in new[] { "29", "361", "", "abc", "90.5" })
        {
            using var response = await Post("20:00", duration: invalidDuration);
            Check(response.StatusCode == HttpStatusCode.OK && await Snapshot() == saved, "invalid duration does not save hours or duration: " + invalidDuration);
        }
        using (var response = await Post("08:00", duration: "180"))
            Check(response.StatusCode == HttpStatusCode.OK && await Snapshot() == saved, "invalid hours cannot save new duration");
        using var noToken = await client.PostAsync("/OpeningHours", new FormUrlEncodedContent(new Dictionary<string,string>()));
        Check(noToken.StatusCode == HttpStatusCode.BadRequest, "antiforgery enforced");
    }
}
