using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S4-01 Task 1 (SQL Server thật + web thật): màn hình "Tạm tính" của bàn đang phục vụ (docs/S4-01-Task1.md).
/// Một bàn gọi nhiều món (số lượng khác nhau, một món có ghi chú), một món huỷ trước khi nấu, một món huỷ sau khi nấu:
/// - API /api/tables/{mã}/provisional-bill và màn hình /ProvisionalBill/Preview/{mã} có đúng số lượng, đơn giá, thành tiền, ghi chú;
///   món huỷ trước khi nấu không xuất hiện; món huỷ sau khi nấu nằm ở nhóm "Đã huỷ – có tính tiền"; tổng cộng đúng và khớp
///   dbo.vw_SessionTotals, chi tiết bàn; tiền định dạng "1.250.000 đ".
/// - Xem tạm tính không đổi gì: trạng thái bàn, phiên, các dòng món, nhật ký món giữ nguyên; gọi thêm món vẫn thành công,
///   xem lại thì món mới xuất hiện (cuối danh sách), bàn vẫn đang phục vụ.
/// - Bàn có phiên nhưng chưa gọi món → thông báo trống; bàn không có khách → 404 kèm lý do.
/// - Quyền: Quản lý, Phục vụ xem được; Bếp, Thu ngân, khách bị chặn. Chi tiết bàn có nút "Tạm tính"; màn hình có "Quay lại gọi món".
/// Cần các tài khoản manager/waiter/kitchen/cashier do ApiAuthorizationVerification tạo.
/// </summary>
internal static class BillPreviewVerification
{
    internal static async Task Run(string connection)
    {
        var password = ApiAuthorizationVerification.Password;
        await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
        var waiter = await SignIn(web, "waiter", password);
        var manager = await SignIn(web, "manager", password);
        var kitchen = await SignIn(web, "kitchen", password);
        var cashier = await SignIn(web, "cashier", password);
        var guest = NewClient(web);
        var waiterId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'waiter'");
        var managerId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'manager'");
        var kitchenId = await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName=N'kitchen'");

        const string orderable = """
            SELECT m.Id FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
            WHERE m.IsActive=1 AND c.IsActive=1 AND m.IsSoldOut=0 AND m.IsTemporarilyOut=0
            """;
        var dishA = await Scalar(connection, $"SELECT TOP(1) Id FROM ({orderable}) d ORDER BY Id");
        var dishB = await Scalar(connection, $"SELECT TOP(1) Id FROM ({orderable}) d WHERE Id<>{dishA} ORDER BY Id");
        var priciest = await Scalar(connection, $"SELECT TOP(1) d.Id FROM ({orderable}) d JOIN dbo.MenuItems m ON m.Id=d.Id ORDER BY m.Price DESC, d.Id");

        // Bàn 1: 4 dòng — A×2 có ghi chú, B×3 không ghi chú, A×1 (huỷ trước khi nấu), B×1 (huỷ sau khi nấu).
        var code = await OpenTable(connection, managerId, waiterId);
        var sessionId = await Scalar(connection, $"SELECT st.SessionId FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId WHERE t.Code='{code}' AND st.ReleasedAt IS NULL");
        await Submit(connection, sessionId, waiterId, $$"""
            [{"MenuItemId":{{dishA}},"Quantity":2,"Notes":"S401 ít cay, thêm chanh"},
             {"MenuItemId":{{dishB}},"Quantity":3},
             {"MenuItemId":{{dishA}},"Quantity":1,"Notes":"S401 huỷ trước"},
             {"MenuItemId":{{dishB}},"Quantity":1,"Notes":"S401 huỷ sau"}]
            """);
        var ids = await Ids(connection, sessionId);
        Assert(ids.Count == 4, "S4-01: the table has 4 order lines");
        var (noted, plain, freeCancel, chargedCancel) = (ids[0], ids[1], ids[2], ids[3]);
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={freeCancel},@Reason='ChangedMind',@ActorUserId={waiterId};");
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_TransitionOrderItem @OrderItemId={chargedCancel},@ToStatus='Preparing',@ActorUserId={kitchenId};");
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={chargedCancel},@Reason='SoldOut',@ActorUserId={managerId},@ConfirmCharged=1;");
        await Check(connection, $"""
            SELECT CASE WHEN (SELECT ChargeWhenCancelled FROM dbo.OrderItems WHERE Id={freeCancel})=0
                         AND (SELECT ChargeWhenCancelled FROM dbo.OrderItems WHERE Id={chargedCancel})=1
                         AND (SELECT Status FROM dbo.OrderItems WHERE Id={freeCancel})='Cancelled'
                         AND (SELECT Status FROM dbo.OrderItems WHERE Id={chargedCancel})='Cancelled' THEN 1 ELSE 0 END
            """, "S4-01: one dish is cancelled before cooking (free) and one after cooking (charged)");

        long Amount(long id) => Scalar(connection, $"SELECT CONVERT(bigint,UnitPrice*Quantity) FROM dbo.OrderItems WHERE Id={id}").GetAwaiter().GetResult();
        long Price(long id) => Scalar(connection, $"SELECT CONVERT(bigint,UnitPrice) FROM dbo.OrderItems WHERE Id={id}").GetAwaiter().GetResult();
        var expectedTotal = Amount(noted) + Amount(plain) + Amount(chargedCancel);
        var dbSubtotal = await Scalar(connection, $"SELECT CONVERT(bigint,COALESCE(SUM(Subtotal),0)) FROM dbo.vw_SessionTotals WHERE BillingSessionId={sessionId}");
        Assert(dbSubtotal == expectedTotal, $"S4-01: the expected total ({expectedTotal}) equals dbo.vw_SessionTotals ({dbSubtotal})");

        var before = await Snapshot(connection, code, sessionId);

        // 1. API: dòng món, nhóm huỷ có tính tiền, tổng cộng.
        var api = $"/api/tables/{Uri.EscapeDataString(code)}/provisional-bill";
        var bill = await Json(waiter, api);
        var lines = bill.GetProperty("lines").EnumerateArray().ToArray();
        var group = bill.GetProperty("chargedCancelled");
        var groupLines = group.GetProperty("lines").EnumerateArray().ToArray();
        Assert(lines.Select(l => l.GetProperty("id").GetInt64()).SequenceEqual(new[] { noted, plain })
               && lines[0].GetProperty("quantity").GetInt32() == 2 && lines[1].GetProperty("quantity").GetInt32() == 3
               && lines[0].GetProperty("unitPrice").GetDecimal() == Price(noted) && lines[1].GetProperty("unitPrice").GetDecimal() == Price(plain)
               && lines[0].GetProperty("amount").GetDecimal() == Amount(noted) && lines[1].GetProperty("amount").GetDecimal() == Amount(plain)
               && lines[0].GetProperty("amountText").GetString() == Vnd(Amount(noted)) && lines[1].GetProperty("unitPriceText").GetString() == Vnd(Price(plain)),
            "S4-01 API: each line has the right quantity, unit price and line amount (VND text included)");
        Assert(lines[0].GetProperty("note").GetString() == "Ghi chú: S401 ít cay, thêm chanh" && lines[1].GetProperty("note").ValueKind == JsonValueKind.Null,
            "S4-01 API: the dish with a note shows exactly that note; the other has none");
        Assert(lines.Concat(groupLines).All(l => l.GetProperty("id").GetInt64() != freeCancel),
            "S4-01 API: the dish cancelled before cooking does not appear");
        Assert(groupLines.Length == 1 && groupLines[0].GetProperty("id").GetInt64() == chargedCancel
               && group.GetProperty("label").GetString() == "Đã huỷ – có tính tiền" && groupLines[0].GetProperty("statusLabel").GetString() == "Đã huỷ – có tính tiền"
               && group.GetProperty("subtotal").GetDecimal() == Amount(chargedCancel),
            "S4-01 API: the dish cancelled after cooking is in its own group labelled \"Đã huỷ – có tính tiền\"");
        Assert(bill.GetProperty("total").GetDecimal() == expectedTotal && bill.GetProperty("totalText").GetString() == Vnd(expectedTotal)
               && bill.GetProperty("chargedCancellationCountsInTotal").GetBoolean() && !bill.GetProperty("isEmpty").GetBoolean()
               && bill.GetProperty("sessionId").GetInt64() == sessionId && bill.GetProperty("tableStatus").GetString() == "Serving",
            $"S4-01 API: the total {Vnd(expectedTotal)} includes the charged cancellation");

        // 2. Màn hình Tạm tính.
        var previewUrl = $"/ProvisionalBill/Preview/{Uri.EscapeDataString(code)}";
        var page = await Html(waiter, previewUrl);
        string Row(long id) => Regex.Match(page, $"<tr data-preview-line=\"{id}\"[\\s\\S]*?</tr>").Value;
        var chargedGroup = Regex.Match(page, "<tbody class=\"bill-preview-charged\" data-preview-group=\"charged-cancelled\">[\\s\\S]*?</tbody>").Value;
        var itemsGroup = Regex.Match(page, "<tbody data-preview-group=\"items\">[\\s\\S]*?</tbody>").Value;
        Assert(itemsGroup.Contains($"data-preview-line=\"{noted}\"") && itemsGroup.Contains($"data-preview-line=\"{plain}\"")
               && !page.Contains($"data-preview-line=\"{freeCancel}\"") && chargedGroup.Contains($"data-preview-line=\"{chargedCancel}\"")
               && chargedGroup.Contains("Đã huỷ – có tính tiền") && !itemsGroup.Contains($"data-preview-line=\"{chargedCancel}\""),
            "S4-01 screen: served dishes in the table, the free cancellation absent, the charged cancellation in its own labelled group");
        Assert(Row(noted).Contains($"data-quantity=\"2\"") && Row(noted).Contains($"data-amount=\"{Amount(noted)}\"")
               && Row(noted).Contains(Vnd(Price(noted))) && Row(noted).Contains(Vnd(Amount(noted)))
               && Row(noted).Contains("data-preview-note>Ghi chú: S401 ít cay, thêm chanh<") && !Row(plain).Contains("data-preview-note")
               && Row(plain).Contains("data-quantity=\"3\"") && Row(plain).Contains(Vnd(Amount(plain))),
            "S4-01 screen: quantities, unit prices, line amounts and notes are shown in VND format");
        Assert(page.Contains($"data-preview-total=\"{expectedTotal}\">{Vnd(expectedTotal)}<") && page.Contains(">Tổng cộng</th>"),
            "S4-01 screen: the total line shows the right amount");
        Assert(page.Contains($"href=\"/?table={Uri.EscapeDataString(code)}\" data-preview-back>← Quay lại gọi món</a>") && !page.Contains("<form"),
            "S4-01 screen: \"Quay lại gọi món\" goes back to the table details; the screen has no form (read only)");
        var managerPage = await Html(manager, previewUrl);
        Assert(managerPage.Contains($"data-preview-total=\"{expectedTotal}\""), "S4-01 screen: the manager sees the same preview");

        // Chi tiết bàn: nút "Tạm tính" + tạm tính hiện tại khớp.
        var map = await Html(waiter, "/");
        Assert(map.Contains("id=\"details-preview-link\"") && map.Contains("data-preview-url=\"/ProvisionalBill/Preview/\"") && map.Contains(">Tạm tính</a>"),
            "S4-01 table details: the \"Tạm tính\" button is on the table details");
        var details = await Json(waiter, $"/api/table-map/{Uri.EscapeDataString(code)}");
        Assert(details.GetProperty("currentSubtotal").GetDecimal() == expectedTotal && details.GetProperty("status").GetString() == "Serving",
            "S4-01 table details: the current subtotal on the table details equals the preview total");

        // 3. Chỉ đọc: không có gì thay đổi sau khi xem (API + màn hình, nhiều lần).
        for (var i = 0; i < 3; i++) { await Json(waiter, api); await Html(waiter, previewUrl); }
        var after = await Snapshot(connection, code, sessionId);
        Assert(after == before, $"S4-01 read-only: table status, session and order lines are unchanged after viewing ({after})");

        // 4. Quyền.
        foreach (var (client, who) in new[] { (kitchen, "the kitchen"), (cashier, "the cashier"), (guest, "a visitor") })
        {
            using var apiResponse = await client.GetAsync(api);
            using var pageResponse = await client.GetAsync(previewUrl);
            Assert(Blocked(apiResponse) && Blocked(pageResponse), $"S4-01 permissions: {who} cannot open the preview or its API");
        }

        // 5. Gọi thêm món sau khi xem tạm tính: thành công, xem lại thấy món mới; bàn vẫn đang phục vụ.
        await Submit(connection, sessionId, waiterId, $$"""[{"MenuItemId":{{priciest}},"Quantity":10,"Notes":"S401 gọi thêm"}]""");
        var added = await Scalar(connection, $"SELECT Id FROM dbo.OrderItems WHERE Notes=N'S401 gọi thêm'");
        var newTotal = expectedTotal + Amount(added);
        var billAgain = await Json(waiter, api);
        var linesAgain = billAgain.GetProperty("lines").EnumerateArray().ToArray();
        Assert(linesAgain.Length == 3 && linesAgain[^1].GetProperty("id").GetInt64() == added && linesAgain[^1].GetProperty("quantity").GetInt32() == 10
               && billAgain.GetProperty("total").GetDecimal() == newTotal && billAgain.GetProperty("totalText").GetString() == Vnd(newTotal),
            $"S4-01: adding a dish after viewing succeeds and the new dish appears on the next preview (total {Vnd(newTotal)})");
        Assert(Regex.IsMatch(Vnd(newTotal), "^\\d{1,3}(\\.\\d{3})+ đ$"), "S4-01 VND: a large total uses dot thousands separators and \"đ\"");
        var pageAgain = await Html(waiter, previewUrl);
        Assert(pageAgain.Contains($"data-preview-line=\"{added}\"") && pageAgain.Contains($"data-preview-total=\"{newTotal}\">{Vnd(newTotal)}<"),
            "S4-01 screen: the preview shows the newly added dish and the new total");
        await Check(connection, $"""
            SELECT CASE WHEN t.Status='Serving' AND s.Status='Open' THEN 1 ELSE 0 END
            FROM dbo.DiningTables t JOIN dbo.SessionTables st ON st.TableId=t.Id AND st.ReleasedAt IS NULL
            JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE t.Code='{code}'
            """, "S4-01: the table is still being served after viewing the preview and adding a dish");

        // 6. Bàn có phiên nhưng chưa gọi món: thông báo trống.
        var emptyCode = await OpenTable(connection, managerId, waiterId);
        var emptyBill = await Json(waiter, $"/api/tables/{Uri.EscapeDataString(emptyCode)}/provisional-bill");
        var emptyPage = await Html(waiter, $"/ProvisionalBill/Preview/{Uri.EscapeDataString(emptyCode)}");
        Assert(emptyBill.GetProperty("isEmpty").GetBoolean() && emptyBill.GetProperty("emptyMessage").GetString() == "Bàn chưa có món nào."
               && emptyBill.GetProperty("total").GetDecimal() == 0 && emptyPage.Contains("data-preview-empty>Bàn chưa có món nào.<"),
            "S4-01: a table with no dishes yet shows the empty message");

        // 7. Bàn không có khách / không tồn tại: 404 kèm lý do.
        var freeCode = await Text(connection, """
            SELECT TOP(1) t.Code FROM dbo.DiningTables t
            WHERE NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL) ORDER BY t.Id
            """);
        using (var free = await waiter.GetAsync($"/api/tables/{Uri.EscapeDataString(freeCode)}/provisional-bill"))
            Assert(free.StatusCode == HttpStatusCode.NotFound && (await free.Content.ReadAsStringAsync()).Contains("không có khách"),
                "S4-01: a table without guests has no preview (404 with the reason)");
        using (var missing = await waiter.GetAsync("/ProvisionalBill/Preview/ZZ99X"))
            Assert(missing.StatusCode == HttpStatusCode.NotFound && WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()).Contains("Không tìm thấy bàn ZZ99X"),
                "S4-01: an unknown table code shows a not-found message");
        Console.WriteLine($"PASS: S4-01 Task 1 provisional bill preview checks (table {code}, total {Vnd(newTotal)}).");
    }

    /// <summary>Định dạng đã chốt: dấu chấm phân cách hàng nghìn + " đ" (độc lập với BillRules để đối chiếu).</summary>
    private static string Vnd(long amount) => amount.ToString("#,0", new NumberFormatInfo { NumberGroupSeparator = "." }) + " đ";

    internal static async Task<string> OpenTable(string connection, long managerId, long waiterId, long? areaId = null)
    {
        var areaFilter = areaId is null ? "" : "AND t.AreaId=" + areaId.Value;
        var tableId = await Scalar(connection, $$"""
            SELECT TOP(1) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
            WHERE t.IsActive=1 AND a.IsActive=1 AND t.Status='Available' AND t.MaxCapacity>=2 {{areaFilter}}
              AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL)
              AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Pending','Confirmed')
                             AND r.StartsAt<DATEADD(minute,(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1),SYSUTCDATETIME())
                             AND r.EndsAt>DATEADD(minute,-30,SYSUTCDATETIME()))
            ORDER BY t.Id
            """);
        await DatabaseTool.Execute(connection, $"""
            IF NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Status='Open')
                EXEC dbo.usp_OpenShift @Name=N'Ca kiểm thử S4-01 tạm tính', @OpeningCash=0, @ActorUserId={managerId};
            EXEC dbo.usp_OpenSession @TableId={tableId},@GuestCount=2,@ActorUserId={waiterId};
            """);
        return await Text(connection, $"SELECT Code FROM dbo.DiningTables WHERE Id={tableId}");
    }

    internal static Task Submit(string connection, long sessionId, long waiterId, string itemsJson)
    {
        var escaped = itemsJson.Replace("'", "''");
        return DatabaseTool.Execute(connection, $"""
            DECLARE @r uniqueidentifier=NEWID();
            EXEC dbo.usp_SubmitOrder @SessionId={sessionId},@RequestId=@r,@ItemsJson=N'{escaped}',@ActorUserId={waiterId};
            """);
    }

    private static async Task<List<long>> Ids(string connection, long sessionId)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT i.Id FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={sessionId} ORDER BY i.Id", cn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ids = new List<long>();
        while (await reader.ReadAsync()) ids.Add(reader.GetInt64(0));
        return ids;
    }

    /// <summary>Trạng thái + rowversion của bàn, phiên và mọi dòng món / nhật ký món của phiên: đổi bất cứ gì là khác chuỗi.</summary>
    internal static Task<string> Snapshot(string connection, string code, long sessionId) => Text(connection, $"""
        SELECT CONCAT(t.Status,'|',CONVERT(varchar(30),t.StatusChangedAt,126),'|',CONVERT(bigint,t.RowVersion),'|',
               s.Status,'|',CONVERT(bigint,s.RowVersion),'|',
               (SELECT COUNT(*) FROM dbo.OrderBatches b WHERE b.SessionId=s.Id),'|',
               (SELECT COUNT(*) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId=s.Id),'|',
               (SELECT MAX(CONVERT(bigint,i.RowVersion)) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId=s.Id),'|',
               (SELECT COUNT(*) FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId=s.Id),'|',
               (SELECT COUNT(*) FROM dbo.SessionTables st WHERE st.SessionId=s.Id AND st.ReleasedAt IS NULL))
        FROM dbo.DiningTables t JOIN dbo.DiningSessions s ON s.Id={sessionId} WHERE t.Code='{code}'
        """);

    internal static async Task<JsonElement> Json(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert(response.StatusCode == HttpStatusCode.OK, $"Opens {path} ({(int)response.StatusCode})");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    internal static bool Blocked(HttpResponseMessage response)
    {
        var location = Location(response);
        return response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || (response.StatusCode == HttpStatusCode.Redirect && (location.Contains("/Account/AccessDenied") || location.Contains("/Account/Login")));
    }

    internal static async Task<string> Text(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "";
    }

    internal static HttpClient NewClient(Web web)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
    }

    internal static async Task<HttpClient> SignIn(Web web, string user, string password)
    {
        var client = NewClient(web);
        await Login(client, user, password);
        return client;
    }
}
