using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using RestaurantManagement.Web.Controllers;
using RestaurantManagement.Web.Models.Tables;
using RestaurantManagement.Web.Services;

/// <summary>
/// S3-01 Task 1 (không cần database): mã phiên khách, cookie, kết quả quét QR và các nhánh không chạm database.
/// Luồng đầy đủ với SQL Server và web thật: tools/RestaurantManagement.DbTool/QrGuestSessionVerification.cs (lệnh verify-qr-session).
/// </summary>
static class TableQrSessionTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => GuestTableSessionService.CreateGuestToken()).ToList();
        check(tokens.All(GuestTableSessionService.IsValidGuestToken) && tokens.All(t => t.Length == 43),
            "QR session: guest tokens are 43-character base64url values");
        check(tokens.Distinct(StringComparer.Ordinal).Count() == tokens.Count, "QR session: 1000 guest tokens are all different");
        check(!GuestTableSessionService.IsValidGuestToken(null) && !GuestTableSessionService.IsValidGuestToken("")
            && !GuestTableSessionService.IsValidGuestToken(new string('a', 42)) && !GuestTableSessionService.IsValidGuestToken(new string('a', 44))
            && !GuestTableSessionService.IsValidGuestToken(new string('a', 42) + "="),
            "QR session: malformed guest cookies are ignored before reaching the database");
        check(GuestTableSessionService.Hash(tokens[0]).Length == 32 && !GuestTableSessionService.Hash(tokens[0]).SequenceEqual(GuestTableSessionService.Hash(tokens[1])),
            "QR session: only a 32-byte SHA-256 of the guest token is stored");

        var expires = new DateTime(2026, 10, 9, 15, 0, 0, DateTimeKind.Utc);
        var opensOrdering = new[] { QrStartOutcome.Started, QrStartOutcome.Rejoined, QrStartOutcome.Joined, QrStartOutcome.Resumed };
        foreach (var outcome in Enum.GetValues<QrStartOutcome>())
        {
            var result = new QrStartResult(outcome, outcome is QrStartOutcome.Started or QrStartOutcome.Rejoined or QrStartOutcome.Joined or QrStartOutcome.Resumed ? tokens[0] : null,
                outcome is QrStartOutcome.Started or QrStartOutcome.Rejoined or QrStartOutcome.Joined or QrStartOutcome.Resumed ? expires : null);
            check(result.OpensOrdering == opensOrdering.Contains(outcome), $"QR session: {outcome} {(result.OpensOrdering ? "opens" : "does not open")} the ordering page");
            check(result.IssuesCookie == (outcome is QrStartOutcome.Started or QrStartOutcome.Rejoined or QrStartOutcome.Joined),
                $"QR session: {outcome} {(result.IssuesCookie ? "sets a new" : "sets no new")} guest cookie");
        }

        foreach (var (outcome, title, status) in new[]
        {
            (QrStartOutcome.TableReserved, "Bàn đã được đặt trước", 409), (QrStartOutcome.TableBusy, "Bàn đang được phục vụ", 409),
            (QrStartOutcome.TableCleaning, "Bàn đang được dọn", 409), (QrStartOutcome.NoShift, "Nhà hàng chưa nhận gọi món", 409),
            (QrStartOutcome.OutsideOpeningHours, "Nhà hàng đang ngoài giờ hoạt động", 409),
            (QrStartOutcome.AwaitingPayment, "Bàn đang chờ thanh toán", 409),
            (QrStartOutcome.SystemBusy, "Hệ thống đang bận", 503)
        })
        {
            var model = TableQrUnavailableViewModel.For(outcome);
            check(model.Title == title && model.StatusCode == status && model.Message.Length > 0 && model.CanRetry == (outcome == QrStartOutcome.SystemBusy),
                $"QR session: {outcome} shows \"{title}\" ({status})");
        }
        var threw = false;
        try { TableQrUnavailableViewModel.For(QrStartOutcome.Started); } catch (ArgumentOutOfRangeException) { threw = true; }
        check(threw, "QR session: success outcomes never render the unavailable page");

        var http = new DefaultHttpContext();
        var plain = GuestTableSessionService.CreateCookieOptions(http.Request, expires);
        http.Request.Scheme = "https";
        var secure = GuestTableSessionService.CreateCookieOptions(http.Request, expires);
        check(plain is { HttpOnly: true, SameSite: SameSiteMode.Lax, IsEssential: true, Path: "/", Secure: false } && secure.Secure
            && plain.Expires == new DateTimeOffset(expires), "QR session: guest cookie is HttpOnly, SameSite=Lax, secure on HTTPS and expires with the session");

        // Các nhánh trả lời trước khi đọc database (chuỗi kết nối rỗng: chạm database sẽ báo lỗi).
        var configuration = new ConfigurationBuilder().Build();
        var guestSessions = new GuestTableSessionService(configuration);
        check((await guestSessions.StartAsync("mã sai!", null, CancellationToken.None)).Outcome == QrStartOutcome.InvalidQr,
            "QR session: a malformed QR code is rejected without touching the database");
        check(await guestSessions.GetContextAsync(null, CancellationToken.None) is null
            && await guestSessions.GetContextAsync("not-a-token", CancellationToken.None) is null,
            "QR session: no or malformed cookie means no ordering session");

        var qrController = new TableQrController(new TableQrService(configuration), guestSessions)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        check(await qrController.Start("x", CancellationToken.None) is ViewResult { ViewName: "NotFound" }
            && qrController.Response.StatusCode == StatusCodes.Status404NotFound
            && !qrController.Response.Headers.SetCookie.Any(),
            "QR session: posting an invalid QR code returns 404 and sets no cookie");

        var orderController = new TableOrderController(guestSessions, null!)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        check(await orderController.Index(CancellationToken.None) is ViewResult { ViewName: "NoSession" }
            && orderController.Response.Headers.CacheControl.ToString() == "no-store",
            "QR session: the ordering page without a scanned QR asks to scan the table QR and is not cached");

        var view = new TableOrderViewModel(new GuestOrderingContext(1, "A05", "Tầng một", 2, 4, "Standard",
            new DateTime(2026, 10, 9, 11, 30, 0, DateTimeKind.Utc)), []);
        check(view.OpenedAtLabel == "18:30" && view.CapacityLabel == "2 – 4 chỗ" && view.TableTypeLabel == "Bàn thường" && view.MenuIsEmpty,
            "QR session: the ordering page shows the table, capacity and Vietnam opening time");

        // Giỏ món gửi từ điện thoại khách.
        var cart = TableOrderController.ParseCart("[{\"dishId\":3,\"quantity\":2,\"notes\":\"ít cay\"},{\"DishId\":5,\"Quantity\":1}]");
        check(cart is { Count: 2 } && cart[0] == new GuestCartLine(3, 2, "ít cay") && cart[1] == new GuestCartLine(5, 1, null),
            "QR order: the cart JSON is read with dish, quantity and optional note");
        check(TableOrderController.ParseCart(null) is null && TableOrderController.ParseCart("[]") is null
            && TableOrderController.ParseCart("không phải json") is null && TableOrderController.ParseCart("[{\"dishId\":\"x\"}]") is null,
            "QR order: empty or malformed carts are rejected");
        foreach (var (lines, label) in new[]
        {
            (new[] { new GuestCartLine(3, 0, null) }, "quantity 0"), (new[] { new GuestCartLine(3, 100, null) }, "quantity 100"),
            (new[] { new GuestCartLine(0, 1, null) }, "unknown dish"), (new[] { new GuestCartLine(3, 1, new string('a', 201)) }, "201-character note")
        })
            check(!(await guestSessions.SubmitOrderAsync(tokens[0], 1, Guid.NewGuid(), lines, CancellationToken.None)).Succeeded,
                $"QR order: {label} is rejected before reaching the database");
        check(!(await guestSessions.SubmitOrderAsync(null, 1, Guid.NewGuid(), [new GuestCartLine(3, 1, null)], CancellationToken.None)).Succeeded,
            "QR order: ordering without a guest session is rejected");
        var orderedView = new TableOrderViewModel(view.Table, [])
        {
            OrderedItems =
            [
                new GuestOrderedItem(1, DateTime.UtcNow, "Gỏi cuốn", "Phần", 40000, 2, null, "Pending", true),
                new GuestOrderedItem(1, DateTime.UtcNow, "Salad", "Đĩa", 20000, 1, null, "Cancelled", false)
            ]
        };
        check(orderedView.OrderedTotal == 80000 && orderedView.OrderedItems[0].StatusLabel == "Chờ bếp nhận"
            && orderedView.OrderedItems[1].StatusLabel == "Đã huỷ" && orderedView.OrderedBatches.Count() == 1,
            "QR order: the ordered list totals charged dishes and labels kitchen statuses");

        // S3-01 Task 2: phân biệt ai đã gọi từng lượt trong phiên chung của bàn.
        check(new GuestOrderedItem(1, DateTime.UtcNow, "A", "Phần", 1, 1, null, "Pending", true, OrderSource.ThisPhone).SourceLabel == "Bạn gọi"
            && new GuestOrderedItem(1, DateTime.UtcNow, "A", "Phần", 1, 1, null, "Pending", true, OrderSource.OtherGuest).SourceLabel == "Khách cùng bàn gọi"
            && new GuestOrderedItem(1, DateTime.UtcNow, "A", "Phần", 1, 1, null, "Pending", true, OrderSource.Staff).SourceLabel == "Nhân viên gọi",
            "QR join: each batch shows who ordered it (this phone, another guest at the table, staff)");

        // S3-01 Task 3: mã đã thay / bàn đang dọn và hướng dẫn gọi phục vụ.
        var replaced = new TableQrLookup(1, "A05", "Tầng một", 1, 4, "Standard", true, true, "Serving", 1, 2);
        var cleaningTable = new TableQrLookup(1, "A05", "Tầng một", 1, 4, "Standard", true, false, "Cleaning", 2, 2);
        check(replaced.IsReplaced && !replaced.IsCleaning && cleaningTable.IsCleaning && !cleaningTable.IsReplaced,
            "QR blocked: an old QR version is detected as replaced and a cleaning table as cleaning");
        check(new CallStaffViewModel("A05", "0280000000").PhoneHref == "tel:0280000000"
            && new CallStaffViewModel("A05", null).PhoneHref is null && new CallStaffViewModel(null, "028 000").PhoneHref is null,
            "QR blocked: the call-staff button only uses a digits-only restaurant phone");
        var cleaningNotice = TableQrUnavailableViewModel.For(QrStartOutcome.TableCleaning) with { TableCode = "A05" };
        check(cleaningNotice.StatusCode == 409 && cleaningNotice.Message.Contains("gọi phục vụ") && !cleaningNotice.CanRetry && cleaningNotice.TableCode == "A05",
            "QR blocked: the cleaning notice asks the guest to call staff and offers no retry");
    }
}
