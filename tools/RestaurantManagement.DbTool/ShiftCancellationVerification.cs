using System.Data;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

internal static class ShiftCancellationVerification
{
    internal static async Task Empty(string connection)
    {
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        await using var cmd = new SqlCommand("EXEC dbo.usp_ShiftCancellationReport @ActorUserId=1", cn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert(!await reader.ReadAsync(), "Task 2: no shifts returns an empty shift list");
        await reader.NextResultAsync(); await reader.ReadAsync();
        Assert(reader.IsDBNull(0), "Task 2: no shifts has no selected shift");
        await reader.NextResultAsync();
        Assert(!await reader.ReadAsync(), "Task 2: no shifts returns no cancellations");
    }

    internal static async Task Run(string connection, HttpClient existingClient, long session, int manager, int waiter, string password)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = existingClient.BaseAddress };
        async Task<JsonDocument> Report(string path)
        {
            using var response = await client.GetAsync(path);
            Assert(response.StatusCode == HttpStatusCode.OK, "Task 2: manager can read report data");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        await Login(client, "manager", password);
        var page = await Html(client, "/ShiftReports");
        Assert(page.Contains("data-nav-item=\"shift-reports\"") && page.Contains("shift-cancellation-report.js"),
            "Task 2: manager sees report menu and interactive report screen");
        using var report = await Report("/ShiftReports/Cancellations?shiftId=1");
        var entries = report.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert(entries.Length == 6 && entries.Select(e => e.GetProperty("eventId").GetInt64()).Distinct().Count() == 6,
            "Task 2: successful cancellations appear once, rejected and repeated requests are excluded");
        Assert(entries.All(e => e.GetProperty("sessionId").GetInt64() == session
            && e.GetProperty("batchId").GetInt64() > 0 && e.GetProperty("orderItemId").GetInt64() > 0
            && e.GetProperty("quantity").GetInt32() > 0 && e.GetProperty("lineTotal").GetDecimal() > 0
            && e.GetProperty("actor").GetString()!.Length > 0 && e.GetProperty("occurredAt").GetString()!.Length == 19
            && e.GetProperty("reason").GetString() != "Không xác định" && e.GetProperty("tableCode").GetString()!.Length > 0),
            "Task 2: report contains order, item, actor, time, reason, table and value");
        Assert(entries.Count(e => !e.GetProperty("chargeWhenCancelled").GetBoolean() && e.GetProperty("chargedAmount").GetDecimal() == 0) == 5
            && entries.Count(e => e.GetProperty("chargeWhenCancelled").GetBoolean()
                && e.GetProperty("chargedAmount").GetDecimal() == e.GetProperty("lineTotal").GetDecimal()
                && e.GetProperty("previousStatus").GetString() == "Preparing") == 1,
            "Task 2: free cancellations and billable manager cancellation are distinguished");
        await DatabaseTool.Execute(connection, $"""
            INSERT dbo.Shifts(Name,BusinessDate,OpenedBy,Status,ClosedBy,ClosedAt)
            VALUES(N'Ca trống đã đóng',CONVERT(date,SYSUTCDATETIME()),{manager},'Closed',{manager},SYSUTCDATETIME());
            UPDATE dbo.OrderItemEvents SET OccurredAt=DATEADD(day,1,OccurredAt) WHERE ToStatus='Cancelled';
            """);
        using var sameShift = await Report("/ShiftReports/Cancellations?shiftId=1");
        Assert(sameShift.RootElement.GetProperty("entries").GetArrayLength() == 6,
            "Task 2: cancellation belongs to the order's shift even on a different calendar date");
        using var empty = await Report("/ShiftReports/Cancellations?shiftId=2");
        Assert(empty.RootElement.GetProperty("entries").GetArrayLength() == 0, "Task 2: another shift contains no cancellations");
        var statuses = empty.RootElement.GetProperty("shifts").EnumerateArray().Select(s => s.GetProperty("status").GetString()).ToArray();
        Assert(statuses.Contains("Open") && statuses.Contains("Closed"), "Task 2: both open and closed shifts are selectable");
        using var latest = await Report("/ShiftReports/Cancellations");
        Assert(latest.RootElement.GetProperty("selectedShiftId").GetInt64() == 2, "Task 2: latest opened shift is the default");
        using (var missing = await client.GetAsync("/ShiftReports/Cancellations?shiftId=999999999"))
            Assert(missing.StatusCode == HttpStatusCode.NotFound, "Task 2: missing shift reports an error");
        using (var invalid = await client.GetAsync("/ShiftReports/Cancellations?shiftId=bad"))
            Assert(invalid.StatusCode == HttpStatusCode.BadRequest, "Task 2: invalid shift is rejected");

        var demoSql = await File.ReadAllTextAsync(Path.Combine(DatabaseTool.Root, "database", "seeds", "ShiftCancellationDemo.sql"));
        await DatabaseTool.Execute(connection, demoSql);
        await DatabaseTool.Execute(connection, demoSql);
        using var demos = await Report("/ShiftReports/Cancellations");
        var demoShifts = demos.RootElement.GetProperty("shifts").EnumerateArray()
            .Where(s => s.GetProperty("name").GetString() == "DEMO S3-05 - Nhật ký huỷ món").ToArray();
        Assert(demoShifts.Length == 1, "Task 2: repeated demo seed creates only one report shift");
        using var demoReport = await Report("/ShiftReports/Cancellations?shiftId=" + demoShifts[0].GetProperty("id").GetInt64());
        var demoEntries = demoReport.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert(demoEntries.Length == 4 && demoEntries.Count(e => e.GetProperty("chargeWhenCancelled").GetBoolean()) == 1
            && demoEntries.Where(e => !e.GetProperty("chargeWhenCancelled").GetBoolean()).Sum(e => e.GetProperty("lineTotal").GetDecimal()) == 200000
            && demoEntries.Sum(e => e.GetProperty("chargedAmount").GetDecimal()) == 60000,
            "Task 2: demo contains three free cancellations and one charged cancellation with correct totals");
        foreach (var name in new[] { "waiter", "kitchen", "cashier" })
        {
            using var roleHandler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var roleClient = new HttpClient(roleHandler) { BaseAddress = client.BaseAddress };
            await Login(roleClient, name, password);
            foreach (var path in new[] { "/ShiftReports", "/ShiftReports/Cancellations?shiftId=1" })
            {
                using var denied = await roleClient.GetAsync(path);
                Assert(denied.StatusCode == HttpStatusCode.Forbidden, $"Task 2: {name} denied {path}");
            }
            var ownPage = await Html(roleClient, name == "waiter" ? "/Ordering" : name == "kitchen" ? "/Kitchen" : "/Cashier");
            Assert(!ownPage.Contains("data-nav-item=\"shift-reports\""), $"Task 2: {name} sees no report menu");
        }
        await using var cn2 = new SqlConnection(connection); await cn2.OpenAsync();
        foreach (var actor in new[] { waiter, 3, 4 })
        {
            try
            {
                await using var cmd = new SqlCommand($"EXEC dbo.usp_ShiftCancellationReport @ActorUserId={actor},@ShiftId=1", cn2);
                await cmd.ExecuteNonQueryAsync(); throw new Exception("Non-manager read SQL report");
            }
            catch (SqlException ex) when (ex.Number == 51510) { Assert(true, "Task 2: SQL denies non-manager despite Reports.Read"); }
        }
        await DatabaseTool.Execute(connection, $"UPDATE dbo.Users SET IsActive=0 WHERE Id={manager};");
        try
        {
            await using var cmd = new SqlCommand($"EXEC dbo.usp_ShiftCancellationReport @ActorUserId={manager}", cn2);
            await cmd.ExecuteNonQueryAsync(); throw new Exception("Inactive manager read SQL report");
        }
        catch (SqlException ex) when (ex.Number == 51510) { Assert(true, "Task 2: inactive manager is denied by SQL"); }
        await DatabaseTool.Execute(connection, $"UPDATE dbo.Users SET IsActive=1 WHERE Id={manager};");
        Console.WriteLine("PASS: S3-05 Task 2 shift report, successful events, charge rules and manager-only access.");
    }
}
