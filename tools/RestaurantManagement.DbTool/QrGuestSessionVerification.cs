using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S3-01 Task 1 (web thật, SQL Server thật): khách quét QR hợp lệ của bàn trống → mở đúng trang gọi món của bàn,
/// tạo đúng MỘT phiên mới, bàn chuyển "Đang phục vụ", không cần đăng nhập.
/// Mỗi "điện thoại" là một HttpClient riêng, không đăng nhập, có hộp cookie riêng.
/// </summary>
internal static class QrGuestSessionVerification
{
    /// <summary>Thứ trong tuần hôm nay theo giờ Việt Nam, cùng công thức với dbo.fn_IsWithinOpeningHours.</summary>
    private const string TodayWeekday =
        "((DATEDIFF(day,CONVERT(date,'19000101'),CONVERT(date,CONVERT(datetime2(3),(SYSUTCDATETIME() AT TIME ZONE 'UTC') AT TIME ZONE 'SE Asia Standard Time')))%7)+1)";

    internal static async Task Run(string connection)
    {
        // Giờ hoạt động phụ thuộc đồng hồ thật: kiểm thử tự đặt giờ của hôm nay rồi trả lại cấu hình cũ.
        await DatabaseTool.Execute(connection, "IF OBJECT_ID('dbo.S301OpeningHoursBackup') IS NOT NULL DROP TABLE dbo.S301OpeningHoursBackup; SELECT * INTO dbo.S301OpeningHoursBackup FROM dbo.OpeningHours;");
        try { await RunScenarios(connection); }
        finally
        {
            await DatabaseTool.Execute(connection, "DELETE dbo.OpeningHours; INSERT dbo.OpeningHours(DayOfWeek,IsClosed,OpensAt,ClosesAt) SELECT DayOfWeek,IsClosed,OpensAt,ClosesAt FROM dbo.S301OpeningHoursBackup; DROP TABLE dbo.S301OpeningHoursBackup;");
        }
    }

    private static Task SetToday(string connection, bool open) => DatabaseTool.Execute(connection, $"""
        DECLARE @day tinyint={TodayWeekday};
        IF NOT EXISTS(SELECT 1 FROM dbo.OpeningHours WHERE DayOfWeek=@day) INSERT dbo.OpeningHours(DayOfWeek,IsClosed) VALUES(@day,1);
        UPDATE dbo.OpeningHours SET IsClosed={(open ? 0 : 1)},
            OpensAt={(open ? "'00:00:00'" : "NULL")}, ClosesAt={(open ? "'23:59:59'" : "NULL")} WHERE DayOfWeek=@day;
        """);

    private static async Task RunScenarios(string connection)
    {
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var used = new List<int>();

        // 0a. Ngoài giờ hoạt động (hôm nay nghỉ): quét QR không mở được phiên, không ghi gì.
        await SetToday(connection, open: false);
        var (closedTable, closedCode) = await FreeTable(connection, used);
        var closedToken = await NewQr(connection, closedTable);
        using (var early = Phone(web))
        using (var rejected = await Start(early, closedToken, await Html(early, $"/q/{closedToken}")))
            Assert(rejected.StatusCode == HttpStatusCode.Conflict && (await Body(rejected)).Contains("ngoài giờ hoạt động"),
                $"QR: outside opening hours → bàn {closedCode} is not opened and the guest is asked to call staff");
        await Check(connection, $"SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId={closedTable}) AND (SELECT Status FROM dbo.DiningTables WHERE Id={closedTable})='Available' THEN 1 ELSE 0 END",
            "QR: rejected scan writes nothing (no session, table stays Available)");
        await SetToday(connection, open: true);

        // 0b. Trong giờ hoạt động nhưng thu ngân chưa mở ca: vẫn mở được phiên; mở ca sau thì phiên được gắn vào ca.
        if (await Scalar(connection, "SELECT COUNT(*) FROM dbo.Shifts WHERE Status='Open'") == 0)
        {
            using var early = Phone(web);
            using (var started = await Start(early, closedToken, await Html(early, $"/q/{closedToken}")))
                Assert(started.StatusCode == HttpStatusCode.Redirect && Location(started) == "/TableOrder",
                    $"QR: within opening hours bàn {closedCode} opens even before the cashier opens a shift");
            await Check(connection, $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE st.TableId={closedTable} AND s.ShiftId IS NULL AND s.OpenedByQrCodeId IS NOT NULL) AND (SELECT Status FROM dbo.DiningTables WHERE Id={closedTable})='Serving' THEN 1 ELSE 0 END",
                "QR: the session is created without a shift and the table is Serving");
            var cashier = await Scalar(connection, "SELECT TOP(1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Code='Cashier' AND u.IsActive=1 ORDER BY u.Id");
            await DatabaseTool.Execute(connection, $"EXEC dbo.usp_OpenShift @Name=N'S3-01 QR',@OpeningCash=0,@ActorUserId={cashier};");
            await Check(connection, $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId JOIN dbo.Shifts sh ON sh.Id=s.ShiftId WHERE st.TableId={closedTable} AND sh.Status='Open') AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE ShiftId IS NULL AND Status<>'Closed') THEN 1 ELSE 0 END",
                "QR: opening the shift attaches the QR session to it (payable as usual)");
        }

        // 1. Bàn trống: GET chỉ hiển thị bàn (an toàn khi bị tải trước), POST mở phiên rồi chuyển tới trang gọi món.
        var (table, code) = await FreeTable(connection, used);
        var token = await NewQr(connection, table);
        var area = await Text(connection, $"SELECT a.Name FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId WHERE t.Id={table}");
        var dish = await Text(connection, "SELECT TOP(1) v.Name FROM dbo.vw_PublicMenu v JOIN dbo.MenuCategories c ON c.Id=v.CategoryId WHERE c.IsActive=1 ORDER BY v.Id");
        var sessionsBefore = await Scalar(connection, "SELECT COUNT(*) FROM dbo.DiningSessions");

        using var phone = Phone(web);
        var scan = await Html(phone, $"/q/{token}");
        Assert(scan.Contains($"Bạn đang ở bàn {code}") && scan.Contains("data-auto-start=\"true\"")
            && scan.Contains($"action=\"/q/{token}\"") && Token(scan).Length > 0,
            "QR: scanning shows the right table and auto-starts the ordering session for a guest");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.DiningSessions)={sessionsBefore} AND (SELECT Status FROM dbo.DiningTables WHERE Id={table})='Available' THEN 1 ELSE 0 END",
            "QR: opening the link (GET) alone creates no session — link previews cannot open a table");

        using (var start = await Start(phone, token, scan))
        {
            Assert(start.StatusCode == HttpStatusCode.Redirect && Location(start) == "/TableOrder", "QR: valid QR of an empty table goes straight to the ordering page");
            var cookie = SetCookies(start).FirstOrDefault(c => c.StartsWith("RM.TableSession=", StringComparison.Ordinal)) ?? "";
            Assert(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase),
                "QR: the phone receives an HttpOnly guest-session cookie (no login, no app)");
            Assert(SetCookies(start).All(c => !c.StartsWith("RestaurantManagement.Auth=", StringComparison.Ordinal)), "QR: no staff login cookie is created");
        }
        await Check(connection, $"""
            SELECT CASE WHEN
                (SELECT COUNT(*) FROM dbo.DiningSessions)={sessionsBefore + 1}
                AND (SELECT COUNT(*) FROM dbo.SessionTables WHERE TableId={table})=1
                AND EXISTS(SELECT 1 FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId
                           JOIN dbo.TableQrCodes q ON q.Id=s.OpenedByQrCodeId
                           WHERE st.TableId={table} AND st.ReleasedAt IS NULL AND s.Status='Open' AND s.OpenedBy IS NULL
                             AND q.TableId={table} AND q.RevokedAt IS NULL AND s.ReservationId IS NULL)
                AND (SELECT COUNT(*) FROM dbo.GuestSessions g JOIN dbo.SessionTables st ON st.SessionId=g.SessionId WHERE st.TableId={table})=1
            THEN 1 ELSE 0 END
            """, "QR: exactly one new session for the right table, opened by its QR (not by staff), with one guest session");
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT Status FROM dbo.DiningTables WHERE Id={table})='Serving'
                AND EXISTS(SELECT 1 FROM dbo.TableStatusChangeEvents WHERE TableId={table} AND PreviousStatus='Available' AND Status='Serving')
                AND (SELECT StatusChangedAt FROM dbo.DiningTables WHERE Id={table}) >= (SELECT s.OpenedAt FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE st.TableId={table})
            THEN 1 ELSE 0 END
            """, "QR: the table moved Available → Serving when the session was created (status event recorded for the table map)");

        using (var order = await phone.GetAsync("/TableOrder"))
        {
            var html = await Body(order);
            Assert(order.StatusCode == HttpStatusCode.OK && html.Contains($"<h1 id=\"table-order-title\">Bàn {code}</h1>") && html.Contains(area),
                "QR: the ordering page shows the table code and area so the guest can confirm the table");
            Assert(html.Contains($"<h3 class=\"public-dish-name\">{dish}</h3>") && html.Contains("<data value="), "QR: the ordering page lists dishes with prices");
            Assert(!html.Contains("Đăng xuất") && !html.Contains("href=\"/Areas\""), "QR: the ordering page opens without staff login and shows no staff navigation");
            Assert(order.Headers.CacheControl?.NoStore == true, "QR: the ordering page is not cached");
        }

        // 1b. Đặt món từ điện thoại: giỏ → "Đặt món" → bếp nhận lượt gọi của bàn, danh sách "Món đã đặt" hiện món.
        var orderDish = await Scalar(connection, "SELECT TOP(1) v.Id FROM dbo.vw_PublicMenu v JOIN dbo.MenuItems m ON m.Id=v.Id JOIN dbo.MenuCategories c ON c.Id=v.CategoryId WHERE c.IsActive=1 AND v.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY v.Id");
        var orderDishName = await Text(connection, $"SELECT Name FROM dbo.MenuItems WHERE Id={orderDish}");
        var orderPage = await Html(phone, "/TableOrder");
        Assert(orderPage.Contains($"data-add-dish=\"{orderDish}\"") && orderPage.Contains("data-cart-submit") && orderPage.Contains("Bàn chưa đặt món nào"),
            "QR order: the ordering page has add-to-cart buttons, an order button and an empty ordered list");
        var requestId = System.Text.RegularExpressions.Regex.Match(orderPage, "name=\"requestId\" value=\"([0-9a-fA-F-]{36})\"").Groups[1].Value;
        var cartJson = $"[{{\"dishId\":{orderDish},\"quantity\":2,\"notes\":\"S301 ít cay\"}}]";
        for (var attempt = 0; attempt < 2; attempt++) // bấm "Đặt món" 2 lần với cùng mã yêu cầu
            using (var placed = await phone.PostAsync("/TableOrder/Submit", Form(("__RequestVerificationToken", Token(orderPage)), ("requestId", requestId), ("cartJson", cartJson))))
                Assert(placed.StatusCode == HttpStatusCode.Redirect && Location(placed) == "/TableOrder#da-dat", $"QR order: placing the order redirects to the ordered list (click {attempt + 1})");
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.OrderBatches b JOIN dbo.SessionTables st ON st.SessionId=b.SessionId WHERE st.TableId={table})=1
                AND EXISTS(SELECT 1 FROM dbo.OrderBatches b JOIN dbo.SessionTables st ON st.SessionId=b.SessionId JOIN dbo.OrderItems i ON i.BatchId=b.Id
                           WHERE st.TableId={table} AND b.GuestSessionId IS NOT NULL AND b.CreatedBy IS NULL AND i.MenuItemId={orderDish}
                             AND i.Quantity=2 AND i.Notes=N'S301 ít cay' AND i.Status='Pending' AND i.OriginalTableId={table})
            THEN 1 ELSE 0 END
            """, "QR order: one batch (double click is idempotent) for the right table, placed by the guest, pending in the kitchen");
        var ordered = await Html(phone, "/TableOrder");
        Assert(ordered.Contains("Đã gửi món xuống bếp") && ordered.Contains($"2 × {orderDishName}") && ordered.Contains("Chờ bếp nhận") && ordered.Contains("S301 ít cay"),
            "QR order: the ordered list shows the dish, quantity, note and kitchen status");
        using (var emptyCart = await phone.PostAsync("/TableOrder/Submit", Form(("__RequestVerificationToken", Token(ordered)), ("requestId", Guid.NewGuid().ToString()), ("cartJson", "[]"))))
            Assert(emptyCart.StatusCode == HttpStatusCode.Redirect && (await Html(phone, "/TableOrder")).Contains("Giỏ món đang trống"), "QR order: an empty cart is rejected with a message");
        using (var stranger = Phone(web))
        using (var noCookie = await stranger.PostAsync("/TableOrder/Submit", Form(("__RequestVerificationToken", Token(await Html(stranger, $"/q/{token}"))), ("requestId", Guid.NewGuid().ToString()), ("cartJson", cartJson))))
            Assert(noCookie.StatusCode == HttpStatusCode.Redirect && Location(noCookie) == "/TableOrder", "QR order: a phone without the table session cannot place orders");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.OrderBatches b JOIN dbo.SessionTables st ON st.SessionId=b.SessionId WHERE st.TableId={table})=1 THEN 1 ELSE 0 END",
            "QR order: rejected submissions add nothing");

        // 2. Quét lại trên cùng điện thoại: vào lại đúng phiên, không ghi thêm gì.
        var rescan = await Html(phone, $"/q/{token}");
        using (var again = await Start(phone, token, rescan))
            Assert(again.StatusCode == HttpStatusCode.Redirect && Location(again) == "/TableOrder", "QR: scanning again on the same phone reopens the ordering page");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SessionTables WHERE TableId={table})=1 AND (SELECT COUNT(*) FROM dbo.GuestSessions g JOIN dbo.SessionTables st ON st.SessionId=g.SessionId WHERE st.TableId={table})=1 THEN 1 ELSE 0 END",
            "QR: the repeated scan creates no new session or guest session");

        // 3. Quét nhiều lần trong thời gian ngắn, đồng thời: 5 điện thoại chưa có cookie + 1 điện thoại bấm 3 lần.
        var (burstTable, burstCode) = await FreeTable(connection, used);
        var burstToken = await NewQr(connection, burstTable);
        var servingEventsBefore = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.TableStatusChangeEvents WHERE TableId={burstTable} AND Status='Serving'");
        var phones = Enumerable.Range(0, 6).Select(_ => Phone(web)).ToList();
        try
        {
            var pages = new List<string>();
            foreach (var p in phones) pages.Add(await Html(p, $"/q/{burstToken}"));
            var posts = phones.Take(5).Select((p, i) => Start(p, burstToken, pages[i]))
                .Concat(Enumerable.Range(0, 3).Select(_ => Start(phones[5], burstToken, pages[5])))
                .ToList();
            var responses = await Task.WhenAll(posts);
            try
            {
                Assert(responses.All(r => r.StatusCode == HttpStatusCode.Redirect && Location(r) == "/TableOrder"),
                    "QR: 8 simultaneous scans/clicks of the same QR all land on the ordering page");
            }
            finally { foreach (var r in responses) r.Dispose(); }
            await Check(connection, $"""
                SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SessionTables WHERE TableId={burstTable})=1
                    AND (SELECT COUNT(DISTINCT g.SessionId) FROM dbo.GuestSessions g JOIN dbo.TableQrCodes q ON q.Id=g.TableQrCodeId WHERE q.TableId={burstTable})=1
                    AND (SELECT Status FROM dbo.DiningTables WHERE Id={burstTable})='Serving'
                    AND (SELECT COUNT(*) FROM dbo.TableStatusChangeEvents WHERE TableId={burstTable} AND Status='Serving')={servingEventsBefore + 1}
                THEN 1 ELSE 0 END
                """, "QR: simultaneous scans create exactly one session and one Available → Serving change");
            foreach (var p in phones)
                Assert((await Html(p, "/TableOrder")).Contains($"Bàn {burstCode}</h1>"), $"QR: every scanning phone sees the ordering page of bàn {burstCode}");

            // Hết cửa sổ quét lặp (120 giây): điện thoại mới không được tự vào phiên đang mở (vào chung phiên: task sau),
            // điện thoại đã ở trong phiên vẫn quay lại được.
            await DatabaseTool.Execute(connection, $"UPDATE s SET OpenedAt=DATEADD(minute,-10,s.OpenedAt) FROM dbo.DiningSessions s JOIN dbo.SessionTables st ON st.SessionId=s.Id WHERE st.TableId={burstTable};");
            using var late = Phone(web);
            using (var busy = await Start(late, burstToken, await Html(late, $"/q/{burstToken}")))
                Assert(busy.StatusCode == HttpStatusCode.Conflict && (await Body(busy)).Contains("Bàn đang được phục vụ"),
                    "QR: a new phone scanning a table that is already being served gets no new session");
            using (var back = await Start(phones[0], burstToken, await Html(phones[0], $"/q/{burstToken}")))
                Assert(back.StatusCode == HttpStatusCode.Redirect && Location(back) == "/TableOrder", "QR: a phone already in the session can still reopen it later");
            await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SessionTables WHERE TableId={burstTable})=1 THEN 1 ELSE 0 END",
                "QR: still exactly one session for the table");
        }
        finally { foreach (var p in phones) p.Dispose(); }

        // 4. Bàn không trống: đặt trước, đang dọn, đang giữ cho lượt đặt sắp tới → không tạo phiên, không đổi trạng thái.
        foreach (var (status, message) in new[] { ("Reserved", "Bàn đã được đặt trước"), ("Cleaning", "Bàn đang được dọn") })
        {
            var (busyTable, busyCode) = await FreeTable(connection, used);
            var busyToken = await NewQr(connection, busyTable);
            await DatabaseTool.Execute(connection, $"UPDATE dbo.DiningTables SET Status='{status}' WHERE Id={busyTable};");
            using var guest = Phone(web);
            using (var response = await Start(guest, busyToken, await Html(guest, $"/q/{busyToken}")))
                Assert(response.StatusCode == HttpStatusCode.Conflict && (await Body(response)).Contains(message), $"QR: {status} table {busyCode} is not opened ({message})");
            await Check(connection, $"SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId={busyTable}) AND (SELECT Status FROM dbo.DiningTables WHERE Id={busyTable})='{status}' THEN 1 ELSE 0 END",
                $"QR: {status} table keeps its status and gets no session");
            await DatabaseTool.Execute(connection, $"UPDATE dbo.DiningTables SET Status='Available' WHERE Id={busyTable};");
        }
        var (heldTable, heldCode) = await FreeTable(connection, used);
        var heldToken = await NewQr(connection, heldTable);
        await DatabaseTool.Execute(connection, $"""
            DECLARE @start datetime2(3)=DATEADD(minute,30,DATEADD(minute,DATEDIFF(minute,0,SYSUTCDATETIME()),0));
            INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
            VALUES('S31Q01',N'Giữ bàn QR','0981300001',2,{heldTable},@start,DATEADD(minute,90,@start),'Confirmed');
            """);
        try
        {
            using var guest = Phone(web);
            using (var response = await Start(guest, heldToken, await Html(guest, $"/q/{heldToken}")))
                Assert(response.StatusCode == HttpStatusCode.Conflict && (await Body(response)).Contains("Bàn đã được đặt trước"),
                    $"QR: Available table {heldCode} held for a booking in 30 minutes is not opened by a walk-in scan");
            await Check(connection, $"SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId={heldTable}) AND (SELECT Status FROM dbo.DiningTables WHERE Id={heldTable})='Available' THEN 1 ELSE 0 END",
                "QR: the held table gets no session");
        }
        finally { await DatabaseTool.Execute(connection, "DELETE dbo.Reservations WHERE Code='S31Q01';"); }

        // 5. Mã không hợp lệ / đã đổi; trang gọi món khi chưa quét QR.
        using (var stranger = Phone(web))
        {
            var page = await Html(stranger, $"/q/{token}");
            using (var missing = await Start(stranger, "AAAAAAAAAAAAAAAAAAAAAAAA", page))
                Assert(missing.StatusCode == HttpStatusCode.NotFound, "QR: unknown QR code opens nothing (404)");
            using var noSession = await stranger.GetAsync("/TableOrder");
            Assert(noSession.StatusCode == HttpStatusCode.OK && (await Body(noSession)).Contains("data-table-order=\"no-session\""),
                "QR: the ordering page without a scanned QR asks the guest to scan the table QR");
        }
        await NewQr(connection, table); // Sinh lại QR: mã cũ và phiên khách cũ hết hiệu lực.
        using (var changed = await Start(phone, token, await Html(phone, $"/q/{burstToken}")))
            Assert(changed.StatusCode == HttpStatusCode.Gone && (await Body(changed)).Contains("Mã QR đã thay đổi"), "QR: a regenerated (old) QR opens nothing (410)");
        using (var revoked = await phone.GetAsync("/TableOrder"))
            Assert((await Body(revoked)).Contains("data-table-order=\"no-session\""), "QR: after regenerating the QR the old guest session no longer opens the ordering page");

        Console.WriteLine("PASS: S3-01 Task 1 QR → new ordering session checks.");
    }

    private static HttpClient Phone(Web web) =>
        new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() })
        { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(70) };

    private static Task<HttpResponseMessage> Start(HttpClient phone, string token, string pageWithForm) =>
        phone.PostAsync($"/q/{token}", Form(("__RequestVerificationToken", Token(pageWithForm))));

    private static async Task<string> Body(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    /// <summary>Bàn trống thật sự: đang dùng, Available, không có phiên, không có lượt đặt giữ bàn.</summary>
    private static async Task<(int Id, string Code)> FreeTable(string connection, List<int> used)
    {
        var exclude = used.Count == 0 ? "0" : string.Join(",", used);
        var id = (int)await Scalar(connection, $"""
            SELECT COALESCE((SELECT TOP(1) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
              WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available' AND t.Id NOT IN ({exclude})
                AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL)
                AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Pending','Confirmed'))
              ORDER BY t.Id DESC),0)
            """);
        Assert(id > 0, "QR: found a free table for the scenario");
        used.Add(id);
        return (id, await Text(connection, $"SELECT Code FROM dbo.DiningTables WHERE Id={id}"));
    }

    /// <summary>Tạo (hoặc sinh lại) mã QR công khai cho bàn, như Quản lý bấm "Tạo mã QR"/"Đổi mã QR mới".</summary>
    private static async Task<string> NewQr(string connection, int tableId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var manager = (int)await Scalar(connection, "SELECT TOP(1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Code='Manager' AND u.IsActive=1 ORDER BY u.Id");
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_RotateTableQr", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@TableId", SqlDbType.Int).Value = tableId;
        cmd.Parameters.Add("@TokenHash", SqlDbType.Binary, 32).Value = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        cmd.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = manager;
        cmd.Parameters.Add("@PublicToken", SqlDbType.VarChar, 64).Value = token;
        await cmd.ExecuteNonQueryAsync();
        return token;
    }

    private static async Task<string> Text(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "";
    }
}
