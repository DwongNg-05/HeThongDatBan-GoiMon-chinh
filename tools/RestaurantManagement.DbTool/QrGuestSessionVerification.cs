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

            // S3-01 Task 2: quá 120 giây sau khi mở bàn, điện thoại mới vẫn vào CHUNG phiên đang mở (không tạo phiên mới);
            // điện thoại đã ở trong phiên vẫn quay lại được.
            await DatabaseTool.Execute(connection, $"UPDATE s SET OpenedAt=DATEADD(minute,-10,s.OpenedAt) FROM dbo.DiningSessions s JOIN dbo.SessionTables st ON st.SessionId=s.Id WHERE st.TableId={burstTable};");
            using var late = Phone(web);
            using (var joined = await Start(late, burstToken, await Html(late, $"/q/{burstToken}")))
                Assert(joined.StatusCode == HttpStatusCode.Redirect && Location(joined) == "/TableOrder",
                    "QR: a new phone scanning a table that is already being served joins its current session");
            using (var back = await Start(phones[0], burstToken, await Html(phones[0], $"/q/{burstToken}")))
                Assert(back.StatusCode == HttpStatusCode.Redirect && Location(back) == "/TableOrder", "QR: a phone already in the session can still reopen it later");
            await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SessionTables WHERE TableId={burstTable})=1 THEN 1 ELSE 0 END",
                "QR: still exactly one session for the table");
        }
        finally { foreach (var p in phones) p.Dispose(); }

        await JoinOpenSession(connection, web, used);
        await BlockedScans(connection, web, used);
        await PaidThenRescan(connection, web, used);

        // 4. Bàn không trống: đặt trước, đang dọn, đang giữ cho lượt đặt sắp tới → không tạo phiên, không đổi trạng thái.
        foreach (var (status, message) in new[] { ("Reserved", "Bàn đã được đặt trước") }) // bàn đang dọn: BlockedScans (Task 3)
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

    /// <summary>
    /// S3-01 Task 2: bàn đang phục vụ (nhân viên mở phiên và gọi trước món) → hai khách lần lượt quét cùng QR,
    /// cùng vào phiên hiện tại (không có phiên thứ hai) và cùng thấy đầy đủ các món đã gọi trước đó.
    /// </summary>
    private static async Task JoinOpenSession(string connection, Web web, List<int> used)
    {
        var (table, code) = await FreeTable(connection, used);
        var token = await NewQr(connection, table);
        var waiter = await Scalar(connection, "SELECT TOP(1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Code='Waiter' AND u.IsActive=1 ORDER BY u.Id");
        var dishes = new List<long>();
        foreach (var offset in new[] { 0, 1 })
            dishes.Add(await Scalar(connection, $"SELECT v.Id FROM dbo.vw_PublicMenu v JOIN dbo.MenuItems m ON m.Id=v.Id JOIN dbo.MenuCategories c ON c.Id=v.CategoryId WHERE c.IsActive=1 AND v.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY v.Id OFFSET {offset} ROWS FETCH NEXT 1 ROWS ONLY"));
        var staffDish = await Text(connection, $"SELECT Name FROM dbo.MenuItems WHERE Id={dishes[0]}");
        var guestDish = await Text(connection, $"SELECT Name FROM dbo.MenuItems WHERE Id={dishes[1]}");

        // Nhân viên đón khách (phiên do nhân viên mở) và gọi trước 3 phần món thứ nhất.
        await DatabaseTool.Execute(connection, $$"""
            EXEC dbo.usp_OpenSession @TableId={{table}},@GuestCount=1,@ActorUserId={{waiter}};
            DECLARE @session bigint=(SELECT st.SessionId FROM dbo.SessionTables st WHERE st.TableId={{table}} AND st.ReleasedAt IS NULL);
            DECLARE @request uniqueidentifier=NEWID();
            DECLARE @items nvarchar(max)=CONCAT(N'[{"MenuItemId":',{{dishes[0]}},N',"Quantity":3,"Notes":"S302 nhân viên gọi"}]');
            EXEC dbo.usp_SubmitOrder @SessionId=@session,@RequestId=@request,@ItemsJson=@items,@ActorUserId={{waiter}};
            """);
        var session = await Scalar(connection, $"SELECT SessionId FROM dbo.SessionTables WHERE TableId={table} AND ReleasedAt IS NULL");
        var sessionsBefore = await Scalar(connection, "SELECT COUNT(*) FROM dbo.DiningSessions");

        // Khách thứ nhất quét QR: vào chung phiên, thấy món nhân viên đã gọi, rồi gọi thêm món thứ hai.
        using var first = Phone(web);
        using (var start = await Start(first, token, await Html(first, $"/q/{token}")))
            Assert(start.StatusCode == HttpStatusCode.Redirect && Location(start) == "/TableOrder", $"QR join: guest 1 scanning serving bàn {code} goes to the ordering page");
        var firstPage = await Html(first, "/TableOrder");
        Assert(firstPage.Contains("vào chung phiên gọi món của bàn") && firstPage.Contains($"Bàn {code}</h1>")
            && firstPage.Contains($"3 × {staffDish}") && firstPage.Contains("S302 nhân viên gọi") && firstPage.Contains("Nhân viên gọi"),
            "QR join: guest 1 sees the joined-session notice and the dish the staff ordered earlier (marked as staff)");
        Assert(firstPage.Contains("Món đang chọn") && firstPage.Contains("Chưa gửi bếp") && firstPage.Contains("Món bàn đã gọi"),
            "QR join: dishes being prepared (not sent) are shown apart from dishes already ordered");
        var requestId = System.Text.RegularExpressions.Regex.Match(firstPage, "name=\"requestId\" value=\"([0-9a-fA-F-]{36})\"").Groups[1].Value;
        using (var placed = await first.PostAsync("/TableOrder/Submit", Form(("__RequestVerificationToken", Token(firstPage)), ("requestId", requestId),
                   ("cartJson", $"[{{\"dishId\":{dishes[1]},\"quantity\":2}}]"))))
            Assert(placed.StatusCode == HttpStatusCode.Redirect, "QR join: guest 1 orders more dishes into the shared session");
        Assert((await Html(first, "/TableOrder")).Contains("Bạn gọi"), "QR join: guest 1 sees its own batch marked as \"Bạn gọi\"");

        // Khách thứ hai quét cùng QR: cùng phiên, thấy đủ món của nhân viên và của khách thứ nhất.
        using var second = Phone(web);
        using (var start = await Start(second, token, await Html(second, $"/q/{token}")))
            Assert(start.StatusCode == HttpStatusCode.Redirect && Location(start) == "/TableOrder", "QR join: guest 2 scanning the same QR goes to the ordering page");
        var secondPage = await Html(second, "/TableOrder");
        Assert(secondPage.Contains($"3 × {staffDish}") && secondPage.Contains($"2 × {guestDish}") && secondPage.Contains("Khách cùng bàn gọi")
            && secondPage.Contains("Món bàn đã gọi <span class=\"table-order-count\">(2)</span>"),
            "QR join: guest 2 sees every dish ordered earlier in the session (staff and guest 1)");
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.DiningSessions)={sessionsBefore}
                AND (SELECT COUNT(*) FROM dbo.SessionTables WHERE TableId={table})=1
                AND (SELECT COUNT(*) FROM dbo.GuestSessions WHERE SessionId={session})=2
                AND (SELECT COUNT(*) FROM dbo.OrderBatches WHERE SessionId={session})=2
                AND (SELECT Status FROM dbo.DiningTables WHERE Id={table})='Serving'
            THEN 1 ELSE 0 END
            """, "QR join: no second session — both phones are guest sessions of the one open session, all orders on it");

        // Quét lần tiếp theo (cùng máy) cũng không tạo thêm phiên hay phiên khách.
        using (var again = await Start(second, token, await Html(second, $"/q/{token}")))
            Assert(again.StatusCode == HttpStatusCode.Redirect, "QR join: guest 2 scanning again reopens the page");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.SessionTables WHERE TableId={table})=1 AND (SELECT COUNT(*) FROM dbo.GuestSessions WHERE SessionId={session})=2 THEN 1 ELSE 0 END",
            "QR join: the next scan creates no extra session");

        // Bàn đang chờ thanh toán: khách mới không vào để gọi thêm.
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_SetPaymentState @SessionId={session},@Awaiting=1,@ActorUserId={waiter};");
        using var third = Phone(web);
        using (var paying = await Start(third, token, await Html(third, $"/q/{token}")))
            Assert(paying.StatusCode == HttpStatusCode.Conflict && (await Body(paying)).Contains("Bàn đang chờ thanh toán"),
                "QR join: a table awaiting payment does not take new guests");
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_SetPaymentState @SessionId={session},@Awaiting=0,@ActorUserId={waiter};");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.GuestSessions WHERE SessionId={session})=2 AND (SELECT COUNT(*) FROM dbo.DiningSessions)={sessionsBefore} THEN 1 ELSE 0 END",
            "QR join: the rejected scan writes nothing");
        Console.WriteLine("PASS: S3-01 Task 2 QR → join the open session checks.");
    }

    /// <summary>
    /// S3-01 Task 3: mã QR đã bị sinh lại và bàn đang dọn đều bị chặn, không tạo phiên, khách được đề nghị gọi phục vụ.
    /// </summary>
    private static async Task BlockedScans(string connection, Web web, List<int> used)
    {
        var phoneNumber = await Text(connection, "SELECT Phone FROM dbo.RestaurantSettings WHERE Id=1");
        void CallStaffShown(string html, string? tableCode, string label)
        {
            Assert(html.Contains("data-call-staff") && html.Contains("Vui lòng gọi phục vụ để được hỗ trợ")
                && (tableCode is null || html.Contains($"data-call-staff-table>{tableCode}</strong>"))
                && (string.IsNullOrEmpty(phoneNumber) || html.Contains($"href=\"tel:{phoneNumber}\"")),
                $"{label}: the guest is asked to call staff (table code{(string.IsNullOrEmpty(phoneNumber) ? "" : " and restaurant phone button")})");
            Assert(!html.Contains("id=\"qr-start-form\""), $"{label}: no form that could open a session");
        }

        // a) Bàn đang phục vụ bằng mã phiên bản 1 → Quản lý sinh lại mã (phiên bản 2) → quét mã cũ bị chặn.
        var (table, code) = await FreeTable(connection, used);
        var oldToken = await NewQr(connection, table);
        using var seated = Phone(web);
        using (var start = await Start(seated, oldToken, await Html(seated, $"/q/{oldToken}")))
            Assert(start.StatusCode == HttpStatusCode.Redirect, $"QR blocked: guest opens bàn {code} with the current QR");
        var newToken = await NewQr(connection, table);
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.TableQrCodes WHERE TableId={table} AND RevokedAt IS NULL)=1
                AND (SELECT Version FROM dbo.TableQrCodes WHERE PublicToken='{newToken}') = (SELECT MAX(Version) FROM dbo.TableQrCodes WHERE TableId={table})
                AND (SELECT Version FROM dbo.TableQrCodes WHERE PublicToken='{oldToken}') + 1 = (SELECT Version FROM dbo.TableQrCodes WHERE PublicToken='{newToken}')
                AND (SELECT RevokedAt FROM dbo.TableQrCodes WHERE PublicToken='{oldToken}') IS NOT NULL
            THEN 1 ELSE 0 END
            """, "QR blocked: regenerating stores a new current version and retires the old one");
        var sessionsBefore = await Scalar(connection, "SELECT COUNT(*) FROM dbo.DiningSessions");
        var guestsBefore = await Scalar(connection, "SELECT COUNT(*) FROM dbo.GuestSessions");

        using var stranger = Phone(web);
        using (var oldScan = await stranger.GetAsync($"/q/{oldToken}"))
        {
            var html = await Body(oldScan);
            Assert(oldScan.StatusCode == HttpStatusCode.Gone && html.Contains("Mã QR đã thay đổi"), "QR blocked: opening an old (regenerated) QR shows \"Mã QR đã thay đổi\" (410)");
            CallStaffShown(html, code, "QR blocked (old QR)");
        }
        var formPage = await Html(stranger, $"/q/{newToken}"); // mã chống giả mạo hợp lệ để thử gửi thẳng yêu cầu mở phiên bằng mã cũ
        using (var forced = await Start(stranger, oldToken, formPage))
            Assert(forced.StatusCode == HttpStatusCode.Gone && (await Body(forced)).Contains("Mã QR đã thay đổi"), "QR blocked: posting the old QR directly is also refused (410)");
        using (var seatedOld = await Start(seated, oldToken, await Html(seated, $"/q/{newToken}")))
            Assert(seatedOld.StatusCode == HttpStatusCode.Gone, "QR blocked: a phone that used the old QR cannot reopen a session with it");
        Assert((await Html(seated, "/TableOrder")).Contains("data-table-order=\"no-session\""), "QR blocked: the old QR's guest session no longer opens the ordering page");

        // b) Bàn đang dọn → quét mã hiện tại bị chặn ngay khi mở đường dẫn và cả khi gửi thẳng yêu cầu.
        var (cleaning, cleaningCode) = await FreeTable(connection, used);
        var cleaningToken = await NewQr(connection, cleaning);
        using var guest = Phone(web);
        var cleaningForm = await Html(guest, $"/q/{cleaningToken}");
        await DatabaseTool.Execute(connection, $"UPDATE dbo.DiningTables SET Status='Cleaning' WHERE Id={cleaning};");
        using (var scan = await guest.GetAsync($"/q/{cleaningToken}"))
        {
            var html = await Body(scan);
            Assert(scan.StatusCode == HttpStatusCode.Conflict && html.Contains("Bàn đang được dọn"), $"QR blocked: scanning bàn {cleaningCode} while it is being cleaned shows \"Bàn đang được dọn\" (409)");
            CallStaffShown(html, cleaningCode, "QR blocked (cleaning)");
        }
        using (var forced = await Start(guest, cleaningToken, cleaningForm))
            Assert(forced.StatusCode == HttpStatusCode.Conflict && (await Body(forced)).Contains("Bàn đang được dọn"), "QR blocked: posting directly for a cleaning table is also refused (409)");
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.DiningSessions)={sessionsBefore}
                AND (SELECT COUNT(*) FROM dbo.GuestSessions)={guestsBefore}
                AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId={cleaning})
                AND (SELECT Status FROM dbo.DiningTables WHERE Id={cleaning})='Cleaning'
            THEN 1 ELSE 0 END
            """, "QR blocked: refused scans create no session or guest session and keep the table status");
        await DatabaseTool.Execute(connection, $"UPDATE dbo.DiningTables SET Status='Available' WHERE Id={cleaning};");
        Console.WriteLine("PASS: S3-01 Task 3 regenerated QR / cleaning table checks.");
    }

    /// <summary>
    /// S3-01 Task 4: thanh toán đóng phiên → mã QR vẫn hợp lệ → quét lại (sau khi dọn bàn) mở đúng một phiên mới,
    /// không chứa món của phiên đã thanh toán, bàn chuyển lại "Đang phục vụ".
    /// </summary>
    private static async Task PaidThenRescan(string connection, Web web, List<int> used)
    {
        async Task<long> RoleUser(string role) =>
            await Scalar(connection, $"SELECT TOP(1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Code='{role}' AND u.IsActive=1 ORDER BY u.Id");
        var kitchen = await RoleUser("Kitchen");
        var waiter = await RoleUser("Waiter");
        var cashier = await RoleUser("Cashier");
        var (table, code) = await FreeTable(connection, used);
        var token = await NewQr(connection, table);
        var qrId = await Scalar(connection, $"SELECT Id FROM dbo.TableQrCodes WHERE PublicToken='{token}'");

        // 1. Khách mở phiên bằng QR và gọi món.
        using var phone = Phone(web);
        using (var start = await Start(phone, token, await Html(phone, $"/q/{token}")))
            Assert(start.StatusCode == HttpStatusCode.Redirect, $"QR paid: guest opens bàn {code}");
        var dish = await Scalar(connection, "SELECT TOP(1) v.Id FROM dbo.vw_PublicMenu v JOIN dbo.MenuItems m ON m.Id=v.Id JOIN dbo.MenuCategories c ON c.Id=v.CategoryId WHERE c.IsActive=1 AND v.IsSoldOut=0 AND m.IsTemporarilyOut=0 ORDER BY v.Id DESC");
        var dishName = await Text(connection, $"SELECT Name FROM dbo.MenuItems WHERE Id={dish}");
        var page = await Html(phone, "/TableOrder");
        var requestId = System.Text.RegularExpressions.Regex.Match(page, "name=\"requestId\" value=\"([0-9a-fA-F-]{36})\"").Groups[1].Value;
        using (var placed = await phone.PostAsync("/TableOrder/Submit", Form(("__RequestVerificationToken", Token(page)), ("requestId", requestId),
                   ("cartJson", $"[{{\"dishId\":{dish},\"quantity\":1,\"notes\":\"S304 phiên cũ\"}}]"))))
            Assert(placed.StatusCode == HttpStatusCode.Redirect, "QR paid: guest orders a dish in the first session");
        var oldSession = await Scalar(connection, $"SELECT SessionId FROM dbo.SessionTables WHERE TableId={table} AND ReleasedAt IS NULL");
        Assert((await Html(phone, "/TableOrder")).Contains($"1 × {dishName}"), "QR paid: the first session shows its dish before payment");

        // 2. Bếp nấu xong, phục vụ mang ra, thu ngân thanh toán (thủ tục như màn hình Bếp / Thu ngân).
        await DatabaseTool.Execute(connection, $"""
            DECLARE @item bigint=(SELECT TOP(1) i.Id FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={oldSession});
            EXEC dbo.usp_TransitionOrderItem @OrderItemId=@item,@ToStatus='Preparing',@ActorUserId={kitchen};
            EXEC dbo.usp_TransitionOrderItem @OrderItemId=@item,@ToStatus='Ready',@ActorUserId={kitchen};
            EXEC dbo.usp_TransitionOrderItem @OrderItemId=@item,@ToStatus='Served',@ActorUserId={waiter};
            DECLARE @request uniqueidentifier=NEWID();
            EXEC dbo.usp_Checkout @SessionId={oldSession},@RequestId=@request,@Method='Cash',@ActorUserId={cashier},@CashReceived=100000000;
            """);
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT Status FROM dbo.DiningSessions WHERE Id={oldSession})='Closed'
                AND (SELECT ClosedAt FROM dbo.DiningSessions WHERE Id={oldSession}) IS NOT NULL
                AND EXISTS(SELECT 1 FROM dbo.Invoices WHERE SessionId={oldSession} AND Status='Paid')
                AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId={table} AND ReleasedAt IS NULL)
                AND NOT EXISTS(SELECT 1 FROM dbo.GuestSessions WHERE SessionId={oldSession} AND RevokedAt IS NULL)
                AND (SELECT Status FROM dbo.DiningTables WHERE Id={table})='Cleaning'
                AND EXISTS(SELECT 1 FROM dbo.TableQrCodes WHERE Id={qrId} AND RevokedAt IS NULL AND PublicToken='{token}')
            THEN 1 ELSE 0 END
            """, "QR paid: payment closes the session, frees the table (Cleaning), ends guest sessions and keeps the QR valid");
        var endedPage = await Html(phone, "/TableOrder");
        Assert(endedPage.Contains("data-table-order=\"ended\"") && endedPage.Contains($"Bàn {code} đã thanh toán") && !endedPage.Contains("S304 phiên cũ"),
            "QR paid: the guest's page says the table was paid and no longer shows the old session's dishes");
        using (var cleaning = await phone.GetAsync($"/q/{token}"))
            Assert(cleaning.StatusCode == HttpStatusCode.Conflict, "QR paid: while the table is being cleaned the QR opens nothing (Task 3)");

        // 3. Nhân viên báo dọn xong → bàn trống → quét lại cùng mã QR mở đúng một phiên mới.
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CleanTable @TableId={table},@ActorUserId={waiter};");
        await Check(connection, $"SELECT CASE WHEN (SELECT Status FROM dbo.DiningTables WHERE Id={table})='Available' THEN 1 ELSE 0 END", "QR paid: after cleaning the table is Available");
        var sessionsBefore = await Scalar(connection, "SELECT COUNT(*) FROM dbo.DiningSessions");
        var servingEventsBefore = await Scalar(connection, $"SELECT COUNT(*) FROM dbo.TableStatusChangeEvents WHERE TableId={table} AND PreviousStatus='Available' AND Status='Serving'");
        using (var again = await Start(phone, token, await Html(phone, $"/q/{token}")))
            Assert(again.StatusCode == HttpStatusCode.Redirect && Location(again) == "/TableOrder", "QR paid: scanning the same QR after payment opens the ordering page");
        var newSession = await Scalar(connection, $"SELECT SessionId FROM dbo.SessionTables WHERE TableId={table} AND ReleasedAt IS NULL");
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.DiningSessions)={sessionsBefore + 1}
                AND {newSession}<>{oldSession}
                AND (SELECT Status FROM dbo.DiningSessions WHERE Id={newSession})='Open'
                AND (SELECT OpenedByQrCodeId FROM dbo.DiningSessions WHERE Id={newSession})={qrId}
                AND NOT EXISTS(SELECT 1 FROM dbo.OrderBatches WHERE SessionId={newSession})
                AND (SELECT Status FROM dbo.DiningTables WHERE Id={table})='Serving'
                AND (SELECT COUNT(*) FROM dbo.TableStatusChangeEvents WHERE TableId={table} AND PreviousStatus='Available' AND Status='Serving')={servingEventsBefore + 1}
            THEN 1 ELSE 0 END
            """, "QR paid: exactly one new session (same QR), with no dishes, and the table is Serving again");
        var newPage = await Html(phone, "/TableOrder");
        Assert(newPage.Contains($"Bàn {code}</h1>") && newPage.Contains("Bàn chưa đặt món nào") && !newPage.Contains("S304 phiên cũ") && !newPage.Contains($"× {dishName}"),
            "QR paid: the new session's page does not show any dish of the paid session");
        using var other = Phone(web);
        using (var join = await Start(other, token, await Html(other, $"/q/{token}")))
            Assert(join.StatusCode == HttpStatusCode.Redirect, "QR paid: a second guest joins the new session");
        await Check(connection, $"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.DiningSessions)={sessionsBefore + 1} AND (SELECT COUNT(*) FROM dbo.GuestSessions WHERE SessionId={newSession})=2 THEN 1 ELSE 0 END",
            "QR paid: the second guest joins the new session instead of creating another");
        Console.WriteLine("PASS: S3-01 Task 4 payment closes the session and the QR opens a new one.");
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
