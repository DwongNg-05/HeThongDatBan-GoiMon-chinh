using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S1-05 Task 2 (AC2): lọc nhật ký theo khoảng ngày (giờ Việt Nam) và tài khoản, mặc định 7 ngày gần nhất.
/// Client phải đang đăng nhập bằng quản lý (Id 1); tài khoản waiter có Id 2.
/// Dữ liệu mẫu được chèn với thời điểm cố định (bảng chỉ chặn UPDATE/DELETE, cho phép INSERT).
/// </summary>
internal static class SecurityAuditFilterVerification
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    internal static async Task Run(string connection, HttpClient client)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow + Vn);
        DateTime Local(DateOnly day, int h = 0, int m = 0, int s = 0, int ms = 0) => day.ToDateTime(new TimeOnly(h, m, s, ms));

        // Biên của 7 ngày mặc định: hôm nay và 6 ngày trước, tính theo ngày Việt Nam.
        await Insert(connection, Local(today.AddDays(-6)), 1, "S105T2-D-IN-START");
        await Insert(connection, Local(today.AddDays(-7), 23, 59, 59, 999), 1, "S105T2-D-OUT-BEFORE");
        await Insert(connection, Local(today, 23, 59, 59), 2, "S105T2-D-IN-END");
        await Insert(connection, Local(today.AddDays(1)), 2, "S105T2-D-OUT-AFTER");
        // Dữ liệu cố định tháng 3/2020 cho lọc khoảng ngày và tài khoản.
        var march = (int d) => new DateOnly(2020, 3, d);
        await Insert(connection, Local(march(4), 23, 59, 59, 999), 1, "S105T2-M-0304");
        await Insert(connection, Local(march(5)), 1, "S105T2-M-0305");
        await Insert(connection, Local(march(8), 12), 1, "S105T2-M-0308");
        await Insert(connection, Local(march(10), 23, 59, 59, 999), 1, "S105T2-M-0310");
        await Insert(connection, Local(march(11)), 1, "S105T2-M-0311");
        await Insert(connection, Local(march(15), 9), 1, "S105T2-M-0315");
        await Insert(connection, Local(march(6), 8), 2, "S105T2-W-0306");
        await Insert(connection, Local(march(12), 20), 2, "S105T2-W-0312");
        await Insert(connection, Local(march(7), 9), null, "S105T2-U-0307");

        // 1. Mở màn hình: mặc định 7 ngày gần nhất.
        var html = await Page(client, "/AuditLogs");
        Assert(Regex.IsMatch(html, $"id=\"audit-filter-summary\"[^>]*data-from=\"{today.AddDays(-6):yyyy-MM-dd}\" data-to=\"{today:yyyy-MM-dd}\""),
            "Default filter is the last 7 Vietnam days (today and 6 days before)");
        Assert(html.Contains($"id=\"fromDate\" name=\"fromDate\" class=\"form-control\" value=\"{today.AddDays(-6):yyyy-MM-dd}\"")
            && html.Contains($"id=\"toDate\" name=\"toDate\" class=\"form-control\" value=\"{today:yyyy-MM-dd}\""), "Default dates prefilled in the filter form");
        var defaults = Markers(html, "D");
        Assert(defaults.SetEquals(new[] { "S105T2-D-IN-START", "S105T2-D-IN-END" }), "Default view includes both day boundaries and excludes the day before/after");
        Assert(Markers(html, "[MWU]").Count == 0, "Default view excludes older entries");
        var fromUtc = Local(today.AddDays(-6)) - Vn;
        var toUtc = Local(today.AddDays(1)) - Vn;
        Assert(Times(html).All(t => t >= fromUtc && t < toUtc), "Every default row lies within the 7-day window");
        Assert(Selected(html, "", "Tất cả tài khoản"), "Default account filter is all accounts");
        Assert(Option(html, "1", "manager — Quản lý") && Option(html, "2", "waiter — Phục vụ") && Option(html, "0", "Định danh không tồn tại"),
            "Account filter offers existing accounts and unknown identifiers");

        // 2. Lọc theo khoảng ngày (bao gồm hai đầu, theo giờ Việt Nam).
        html = await Page(client, "/AuditLogs?fromDate=2020-03-05&toDate=2020-03-10");
        Assert(Markers(html, "[MWU]").SetEquals(new[] { "S105T2-M-0305", "S105T2-M-0308", "S105T2-M-0310", "S105T2-W-0306", "S105T2-U-0307" })
            && Total(html) == 5, "Date range returns exactly the entries between 00:00 of the first day and 23:59:59.999 of the last day");
        Assert(IsNewestFirst(html), "Filtered list stays newest first");
        Assert(html.Contains("value=\"2020-03-05\"") && html.Contains("value=\"2020-03-10\""), "Chosen dates stay in the form");

        // 3. Lọc theo tài khoản.
        html = await Page(client, "/AuditLogs?fromDate=2020-03-01&toDate=2020-03-31&userId=2");
        Assert(Markers(html, "[MWU]").SetEquals(new[] { "S105T2-W-0306", "S105T2-W-0312" }) && Total(html) == 2, "Account filter returns only that account");
        Assert(Selected(html, "2", "waiter — Phục vụ") && !Selected(html, "", "Tất cả tài khoản"), "Chosen account stays selected");
        html = await Page(client, "/AuditLogs?fromDate=2020-03-01&toDate=2020-03-31&userId=0");
        Assert(Markers(html, "[MWU]").SetEquals(new[] { "S105T2-U-0307" }), "Unknown-identifier filter returns only unknown accounts");
        html = await Page(client, "/AuditLogs?fromDate=2020-03-01&toDate=2020-03-31&userId=1");
        Assert(Markers(html, "[MWU]").SetEquals(new[] { "S105T2-M-0304", "S105T2-M-0305", "S105T2-M-0308", "S105T2-M-0310", "S105T2-M-0311", "S105T2-M-0315" }),
            "Manager filter over a month returns all manager entries");

        // 4. Kết hợp hai bộ lọc.
        html = await Page(client, "/AuditLogs?fromDate=2020-03-05&toDate=2020-03-10&userId=1");
        Assert(Markers(html, "[MWU]").SetEquals(new[] { "S105T2-M-0305", "S105T2-M-0308", "S105T2-M-0310" }) && Total(html) == 3,
            "Date range and account filters combine");
        Assert(html.Contains("tài khoản: <strong>manager</strong>"), "Summary names the chosen account");

        // 5. Khoảng ngày không có dữ liệu: danh sách rỗng hợp lệ.
        html = await Page(client, "/AuditLogs?fromDate=2019-01-01&toDate=2019-01-07");
        Assert(html.Contains("id=\"audit-empty\"") && html.Contains("Không có sự kiện nào khớp điều kiện lọc.") && !html.Contains("data-audit-id=")
            && Total(html) == 0 && !html.Contains("id=\"audit-filter-errors\""), "Empty date range shows a valid empty list without errors");

        // 6. Điều kiện không hợp lệ và giới hạn 90 ngày.
        html = await Page(client, "/AuditLogs?fromDate=2020-03-10&toDate=2020-03-05");
        Assert(html.Contains("id=\"audit-filter-errors\"") && html.Contains("\"Từ ngày\" phải trước hoặc bằng \"Đến ngày\".") && !html.Contains("data-audit-id="),
            "Reversed dates are rejected");
        html = await Page(client, "/AuditLogs?fromDate=2020-01-01&toDate=2020-03-31");
        Assert(html.Contains("Khoảng ngày tối đa 90 ngày.") && !html.Contains("data-audit-id="), "91-day range is rejected");
        html = await Page(client, "/AuditLogs?fromDate=2020-01-02&toDate=2020-03-31");
        Assert(!html.Contains("id=\"audit-filter-errors\"") && Markers(html, "[MWU]").Count == 9, "Exactly 90 days is allowed");
        html = await Page(client, "/AuditLogs?userId=999999");
        Assert(html.Contains("Tài khoản đã chọn không có trong danh sách.") && !html.Contains("data-audit-id="), "Account outside the list is rejected");
        html = await Page(client, "/AuditLogs?fromDate=not-a-date");
        Assert(html.Contains("Ngày lọc không hợp lệ.") && !html.Contains("data-audit-id="), "Malformed date is rejected");
        await Reject(connection, "EXEC dbo.usp_SecurityAuditList @ActorUserId=1,@FromUtc='2020-03-10',@ToUtcExclusive='2020-03-05';", 51111,
            "Database rejects an inverted time range");

        Console.WriteLine("PASS: S1-05 Task 2 audit log filter checks.");
    }

    private static async Task Insert(string connection, DateTime localVietnam, int? userId, string marker)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            INSERT dbo.SecurityAuditLogs(OccurredAt,UserId,UserName,RoleCode,Action,Detail,IpAddress)
            SELECT @At,u.Id,COALESCE(u.UserName,N'ab*** (không tồn tại)'),r.Code,'PriceChanged',@Marker,'10.0.0.5'
            FROM (SELECT 1 AS One) d LEFT JOIN dbo.Users u ON u.Id=@UserId LEFT JOIN dbo.Roles r ON r.Id=u.RoleId;
            """, cn);
        cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = localVietnam - Vn;
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = (object?)userId ?? DBNull.Value;
        cmd.Parameters.Add("@Marker", SqlDbType.NVarChar, 300).Value = marker;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> Page(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert(response.StatusCode == HttpStatusCode.OK, "Audit screen opens: " + url);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static HashSet<string> Markers(string html, string kind) =>
        Regex.Matches(html, $"S105T2-{kind}-[A-Z0-9-]+").Select(m => m.Value).ToHashSet();

    private static bool Option(string html, string value, string label) =>
        Regex.IsMatch(html, $"<option(?=[^>]*value=\"{Regex.Escape(value)}\")[^>]*>{Regex.Escape(label)}</option>");

    private static bool Selected(string html, string value, string label) =>
        Regex.IsMatch(html, $"<option(?=[^>]*value=\"{Regex.Escape(value)}\")(?=[^>]*selected)[^>]*>{Regex.Escape(label)}</option>");

    private static long Total(string html)
    {
        var match = Regex.Match(html, "data-total=\"(\\d+)\"");
        return match.Success ? long.Parse(match.Groups[1].Value) : -1;
    }

    private static DateTime[] Times(string html) =>
        Regex.Matches(html, "<time datetime=\"([^\"]+)\"")
            .Select(m => DateTime.Parse(m.Groups[1].Value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal))
            .ToArray();

    private static bool IsNewestFirst(string html)
    {
        var times = Times(html);
        return times.Zip(times.Skip(1)).All(p => p.First >= p.Second);
    }

    private static async Task Reject(string connection, string sql, int error, string name)
    {
        try { await DatabaseTool.Execute(connection, sql); }
        catch (SqlException ex) when (ex.Number == error) { Assert(true, name); return; }
        Assert(false, name);
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
