using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S2-09 Task 1 (HTTP thật, SQL Server thật): đặt bàn có email → email xác nhận được gửi ngay với đúng mã đặt bàn,
/// ngày giờ, số khách, địa chỉ quán; trang xác nhận hiển thị đầy đủ mã; nhân viên thấy trạng thái gửi.
/// Lần 1: Email:Host trống nên email được ghi thành tệp (giống hộp thư). Lần 2: máy chủ SMTP không tồn tại → gửi thất bại
/// nhưng lượt đặt bàn và mã đặt bàn vẫn được ghi nhận.
/// S2-09 Task 2: worker của web (Email:RetryPollSeconds=1) tự gửi lại email thất bại: thành công ở lần thử thứ 2 (lần 1);
/// hết 3 lần gửi lại vẫn lỗi thì ghi kết quả cuối cùng là thất bại (lần 2). "Chờ 5 phút" được tua nhanh bằng cách
/// đưa NextAttemptAt về hiện tại, sau khi đã kiểm tra database hẹn đúng 5 phút và worker không gửi sớm.
/// </summary>
internal static class BookingConfirmationVerification
{
    private const string Address = "S2-09 · 12 Lê Lợi, Phường Bến Nghé, Quận 1, TP.HCM";
    private const string FirstAttemptError = "S209T2 lần gửi đầu: 421 máy chủ bận";

    internal static async Task Run(string connection, string password)
    {
        await DatabaseTool.Execute(connection, $"""
            UPDATE dbo.RestaurantSettings SET Name=N'Bếp Nhà',Address=N'{Address}',Phone='0281234567' WHERE Id=1;
            UPDATE dbo.Users SET MustChangePassword=0,FailedLoginCount=0,LockedUntil=NULL,IsActive=1 WHERE UserName IN (N'manager',N'kitchen',N'cashier');
            UPDATE a SET IsActive=1 FROM dbo.Areas a WHERE a.Id=(SELECT TOP(1) t.AreaId FROM dbo.DiningTables t WHERE t.IsActive=1 AND t.MaxCapacity>=4 ORDER BY t.Id);
            """);
        var slots = await FreeSlots(connection, 3);
        // Bếp và thu ngân dùng mật khẩu riêng cho bước này (các bước trước có thể đã đổi mật khẩu của họ).
        const string staffPassword = "S209-Staff-9x";
        await using (var cn = new SqlConnection(connection))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@hash WHERE UserName IN (N'kitchen',N'cashier');", cn);
            cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(staffPassword, workFactor: 10));
            await cmd.ExecuteNonQueryAsync();
        }

        // 1) Gửi thành công (email ghi vào thư mục tạm).
        var mailDir = Path.Combine(Path.GetTempPath(), "rm-booking-email-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mailDir);
        await using (var web = await Web.Start(connection, new() { ["Email__Host"] = "", ["Email__PickupDirectory"] = mailDir, ["Email__RetryPollSeconds"] = "1" }))
        {
            await Login(web.Client, "manager", password);

            // Khách chọn bàn trong danh sách bàn còn trống của khung giờ.
            var (tableId, tableCode) = await FirstFreeTable(web.Client, slots[0].Local, 4);
            var watch = Stopwatch.StartNew();
            var (shown, page) = await Book(web.Client, "Khách S2-09 <b>OK</b>", "0912090901", 4, slots[0].Local, "khach.s209@example.com", tableId);
            var code = await InternalCode(connection, "0912090901");
            watch.Stop();
            Assert(watch.Elapsed < TimeSpan.FromSeconds(60), $"Booking + confirmation email finished in {watch.ElapsedMilliseconds} ms (< 60 s)");
            Assert(shown == tableCode && page.Contains($"data-booking-code=\"{tableCode}\">{tableCode}</p>") && page.Contains("Mã đặt bàn của bạn"),
                $"Confirmation page shows the chosen table code {tableCode} as the booking code");
            Assert(!page.Contains(code), "Confirmation page shows only one code (the internal reference stays hidden)");
            Assert(page.Contains("data-email-outcome=\"Sent\"") && page.Contains("Email xác nhận đã được gửi tới k***9@example.com"), "Confirmation page says the email was sent (masked address)");
            Assert((await Html(web.Client, "/Reservations/Success")).Contains($">{tableCode}</p>"), "Reloading the confirmation page still shows the table code");
            await Check(connection, $"SELECT CASE WHEN r.TableId={tableId} AND r.Status='Pending' AND t.Code='{tableCode}' THEN 1 ELSE 0 END FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Code='{code}'",
                "Reservation holds the chosen table");
            var stillFree = await FreeTables(web.Client, slots[0].Local, 4);
            Assert(!stillFree.Contains(tableId), "Chosen table disappears from the free tables of that time slot");
            var beforeDouble = await Scalar(connection, "SELECT COUNT(*) FROM dbo.Reservations");
            var doublePage = await TryBook(web.Client, "Khách trùng bàn", "0912090904", 2, slots[0].Local, null, tableId);
            Assert(doublePage.Contains("vừa có khách khác đặt") && await Scalar(connection, "SELECT COUNT(*) FROM dbo.Reservations") == beforeDouble,
                "The same table cannot be booked twice in overlapping time");

            var mails = Directory.GetFiles(mailDir, "*.txt");
            Assert(mails.Length == 1, "Exactly one confirmation email delivered");
            var text = await File.ReadAllTextAsync(mails[0]);
            var html = await File.ReadAllTextAsync(mails[0].Replace(".txt", ".html"));
            var when = BookingWhen(slots[0].Local);
            Assert(text.Contains("To: khach.s209@example.com") && text.Contains($"Subject: Xác nhận đặt bàn {tableCode}"), "Email goes to the customer with the table code in the subject");
            foreach (var (body, kind) in new[] { (text, "text"), (html, "HTML") })
                Assert(body.Contains($"Mã đặt bàn") && body.Contains(tableCode) && !body.Contains(code) && body.Contains(when) && body.Contains("4 người") && body.Contains(Address),
                    $"Email ({kind}) has the booking code (= table code {tableCode}), date/time {when}, guest count and restaurant address");
            Assert(!html.Contains("<b>OK</b>"), "Customer name is HTML-encoded in the email");

            await Check(connection, $"""
                SELECT CASE WHEN o.Status='Sent' AND o.AttemptCount=1 AND o.SentAt IS NOT NULL AND o.LastAttemptAt IS NOT NULL AND o.LastError IS NULL
                 AND JSON_VALUE(o.PayloadJson,'$.RestaurantAddress')=N'{Address}' AND JSON_VALUE(o.PayloadJson,'$.Code')='{code}'
                 AND JSON_VALUE(o.PayloadJson,'$.TableCode')='{tableCode}'
                 AND CONVERT(int,JSON_VALUE(o.PayloadJson,'$.GuestCount'))=4 AND r.Status='Pending' THEN 1 ELSE 0 END
                FROM dbo.Reservations r JOIN dbo.EmailOutbox o ON o.ReservationId=r.Id AND o.MessageType='BookingReceived' WHERE r.Code='{code}'
                """, "Send status, booking code, guest count and restaurant address are stored for the reservation");

            var id = await Scalar(connection, $"SELECT Id FROM dbo.Reservations WHERE Code='{code}'");
            var details = await Html(web.Client, $"/Reservations/Details/{id}");
            Assert(details.Contains("Email xác nhận") && details.Contains("Đã gửi thành công") && details.Contains("1/4") && details.Contains("Chờ xác nhận"),
                "Staff sees 'sent' email status separately from the booking status");
            Assert(details.Contains($"data-table-code=\"{tableCode}\">{tableCode}</strong>"), "Staff details show the table code");

            // S1-04 Task 2: đặt bàn là việc của Quản lý và Phục vụ; Bếp và Thu ngân bị chặn ở máy chủ, kể cả chỉ xem.
            foreach (var role in new[] { "kitchen", "cashier" })
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
                using var staff = new HttpClient(handler) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(20) };
                await Login(staff, role, staffPassword);
                foreach (var path in new[] { "/Reservations", $"/Reservations/Details/{id}" })
                {
                    using var blocked = await staff.GetAsync(path);
                    var body = await blocked.Content.ReadAsStringAsync();
                    Assert(blocked.StatusCode == HttpStatusCode.Forbidden && body.Contains("data-access-denied=\"403\"") && !body.Contains(tableCode),
                        $"{role} cannot open {path} (blocked by the server)");
                }
            }

            // Khách không nhập email: vẫn có mã đặt bàn, không gửi gì.
            var (noEmailCode, noEmailPage) = await Book(web.Client, "Khách không email", "0912090902", 2, slots[1].Local, null);
            Assert(noEmailPage.Contains($">{noEmailCode}</p>") && noEmailPage.Contains("data-email-outcome=\"NotRequested\"") && Directory.GetFiles(mailDir, "*.txt").Length == 1,
                "Booking without email shows the code and sends nothing");

            // Quản lý xoá bàn: bàn còn lượt đặt sắp tới thì không xoá được.
            var areasPage = await Html(web.Client, "/Areas");
            using (var blocked = await web.Client.PostAsync($"/Tables/Delete/{tableId}", Form(("__RequestVerificationToken", Token(areasPage)))))
                Assert(blocked.StatusCode == HttpStatusCode.Redirect, "Delete request for a booked table is answered");
            Assert((await Html(web.Client, "/Areas")).Contains($"Bàn {tableCode} còn lượt đặt bàn sắp tới")
                && await Scalar(connection, $"SELECT CONVERT(int,IsActive) FROM dbo.DiningTables WHERE Id={tableId}") == 1,
                "A table with an upcoming booking cannot be deleted");

            // Bếp không huỷ được đặt bàn (chỉ Quản lý).
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
                using var kitchen = new HttpClient(handler) { BaseAddress = web.Client.BaseAddress, Timeout = TimeSpan.FromSeconds(20) };
                await Login(kitchen, "kitchen", staffPassword);
                var kitchenPage = await Html(kitchen, "/Kitchen");
                using var denied = await kitchen.PostAsync($"/Reservations/Cancel/{id}", Form(("reason", "thử"), ("__RequestVerificationToken", Token(kitchenPage))));
                var refused = denied.StatusCode == HttpStatusCode.Forbidden
                    || (denied.StatusCode == HttpStatusCode.Redirect && Location(denied).Contains("AccessDenied"));
                Assert(refused && await Scalar(connection, $"SELECT CASE WHEN Status='Pending' THEN 1 ELSE 0 END FROM dbo.Reservations WHERE Id={id}") == 1,
                    "Kitchen cannot cancel a reservation");
            }

            // Quản lý huỷ đặt bàn: bắt buộc lý do, khách nhận email báo huỷ, bàn được trả lại.
            details = await Html(web.Client, $"/Reservations/Details/{id}");
            Assert(details.Contains("Huỷ đặt bàn</button>"), "Manager sees the cancel button");
            using (var noReason = await web.Client.PostAsync($"/Reservations/Cancel/{id}", Form(("reason", "  "), ("__RequestVerificationToken", Token(details)))))
                Assert(noReason.StatusCode == HttpStatusCode.Redirect, "Cancel without reason is answered");
            Assert((await Html(web.Client, $"/Reservations/Details/{id}")).Contains("Vui lòng nhập lý do huỷ")
                && await Scalar(connection, $"SELECT CASE WHEN Status='Pending' THEN 1 ELSE 0 END FROM dbo.Reservations WHERE Id={id}") == 1,
                "A reason is required to cancel");
            details = await Html(web.Client, $"/Reservations/Details/{id}");
            using (var cancel = await web.Client.PostAsync($"/Reservations/Cancel/{id}", Form(("reason", "Nhà hàng có sự cố điện"), ("__RequestVerificationToken", Token(details)))))
                Assert(cancel.StatusCode == HttpStatusCode.Redirect, "Manager cancels the reservation");
            var cancelledPage = await Html(web.Client, $"/Reservations/Details/{id}");
            Assert(cancelledPage.Contains($"Đã huỷ đặt bàn {tableCode}. Đã gửi email báo huỷ cho khách.") && cancelledPage.Contains("Đã huỷ")
                && cancelledPage.Contains("Nhà hàng có sự cố điện") && !cancelledPage.Contains("Huỷ đặt bàn</button>"),
                "Cancelled reservation shows its status and reason; it cannot be cancelled twice");
            await Check(connection, $"""
                SELECT CASE WHEN r.Status='Cancelled' AND r.CancelReason=N'Nhà hàng có sự cố điện' AND r.CancelledAt IS NOT NULL
                 AND EXISTS(SELECT 1 FROM dbo.ReservationEvents e WHERE e.ReservationId=r.Id AND e.ToStatus='Cancelled' AND e.ActorUserId=(SELECT Id FROM dbo.Users WHERE UserName=N'manager'))
                 AND EXISTS(SELECT 1 FROM dbo.EmailOutbox o WHERE o.ReservationId=r.Id AND o.MessageType='BookingCancelled' AND o.Status='Sent')
                 THEN 1 ELSE 0 END FROM dbo.Reservations r WHERE r.Id={id}
                """, "Cancellation, its reason, the acting manager and the cancellation email are stored");
            var cancelMail = Directory.GetFiles(mailDir, "*.txt").Select(f => File.ReadAllText(f)).Single(t => t.Contains("Subject: Đã huỷ đặt bàn"));
            Assert(cancelMail.Contains($"Subject: Đã huỷ đặt bàn {tableCode}") && cancelMail.Contains("Nhà hàng có sự cố điện") && cancelMail.Contains("To: khach.s209@example.com"),
                "Customer receives a cancellation email with the booking code and reason");
            Assert((await FreeTables(web.Client, slots[0].Local, 4)).Contains(tableId), "Cancelled table is free again for that time slot");

            // Quản lý xoá bàn: bàn đã có lịch sử → ngừng sử dụng; bàn chưa từng dùng → xoá hẳn.
            areasPage = await Html(web.Client, "/Areas");
            using (var archive = await web.Client.PostAsync($"/Tables/Delete/{tableId}", Form(("__RequestVerificationToken", Token(areasPage)))))
                Assert(archive.StatusCode == HttpStatusCode.Redirect, "Manager deletes a table that has history");
            Assert((await Html(web.Client, "/Areas")).Contains($"Bàn {tableCode} đã có lịch sử")
                && await Scalar(connection, $"SELECT CONVERT(int,IsActive) FROM dbo.DiningTables WHERE Id={tableId}") == 0
                && !(await FreeTables(web.Client, slots[0].Local, 4)).Contains(tableId),
                "A table with history is set to 'not in use' (history kept) and is no longer bookable");
            await DatabaseTool.Execute(connection, $"UPDATE dbo.DiningTables SET IsActive=1 WHERE Id={tableId};");
            var spareId = await Scalar(connection, $"""
                INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder)
                SELECT AreaId,'S209-X',1,2,999 FROM dbo.DiningTables WHERE Id={tableId};
                SELECT CONVERT(bigint,SCOPE_IDENTITY());
                """);
            areasPage = await Html(web.Client, "/Areas");
            Assert(areasPage.Contains("S209-X"), "New unused table is listed");
            using (var delete = await web.Client.PostAsync($"/Tables/Delete/{spareId}", Form(("__RequestVerificationToken", Token(areasPage)))))
                Assert(delete.StatusCode == HttpStatusCode.Redirect, "Manager deletes an unused table");
            Assert((await Html(web.Client, "/Areas")).Contains("Đã xoá bàn S209-X.")
                && await Scalar(connection, $"SELECT COUNT(*) FROM dbo.DiningTables WHERE Id={spareId}") == 0, "Unused table is deleted completely");

            // S2-09 Task 2: lần gửi đầu thất bại → worker tự gửi lại → thành công ở lần thử thứ 2.
            var retryId = await Scalar(connection, $"SELECT Id FROM dbo.Reservations WHERE Code='{await InternalCode(connection, "0912090902")}'");
            await DatabaseTool.Execute(connection, $"""
                UPDATE dbo.Reservations SET Email=N'gui.lai.s209@example.com' WHERE Id={retryId};
                EXEC dbo.usp_QueueBookingEmail @ReservationId={retryId},@Kind='BookingReceived';
                DECLARE @e bigint=(SELECT Id FROM dbo.EmailOutbox WHERE ReservationId={retryId} AND MessageType='BookingReceived');
                BEGIN TRANSACTION;
                UPDATE dbo.EmailOutbox SET AttemptCount=1,LastAttemptAt=DATEADD(minute,-5,SYSUTCDATETIME()),LastError=N'{FirstAttemptError}',
                 NextAttemptAt=DATEADD(year,1,SYSUTCDATETIME()) WHERE Id=@e;
                INSERT dbo.EmailAttempts(EmailId,AttemptNumber,Status,StartedAt,CompletedAt,Error)
                 SELECT Id,1,'Failed',LastAttemptAt,LastAttemptAt,LastError FROM dbo.EmailOutbox WHERE Id=@e;
                EXEC dbo.usp_SyncReservationEmailStatus @e;
                COMMIT;
                """);
            await Check(connection, $"SELECT CASE WHEN ConfirmationEmailStatus='Retrying' AND ConfirmationEmailAttempts=1 THEN 1 ELSE 0 END FROM dbo.Reservations WHERE Id={retryId}",
                "Retry: after a failed first attempt the reservation shows the email is waiting to be sent again");
            await DatabaseTool.Execute(connection, $"UPDATE dbo.EmailOutbox SET NextAttemptAt=SYSUTCDATETIME() WHERE ReservationId={retryId} AND MessageType='BookingReceived';");
            await WaitFor(connection, $"SELECT CASE WHEN Status='Sent' THEN 1 ELSE 0 END FROM dbo.EmailOutbox WHERE ReservationId={retryId} AND MessageType='BookingReceived'",
                "Retry: the web worker sends the email again after the first attempt failed");
            await Check(connection, $"""
                SELECT CASE WHEN o.AttemptCount=2 AND o.LastError IS NULL AND o.SentAt IS NOT NULL
                 AND (SELECT COUNT(*) FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id)=2
                 AND EXISTS(SELECT 1 FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id AND a.AttemptNumber=1 AND a.Status='Failed' AND a.Error=N'{FirstAttemptError}')
                 AND EXISTS(SELECT 1 FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id AND a.AttemptNumber=2 AND a.Status='Succeeded' AND a.CompletedAt IS NOT NULL AND a.Error IS NULL)
                 AND r.ConfirmationEmailStatus='Sent' AND r.ConfirmationEmailAttempts=2 THEN 1 ELSE 0 END
                FROM dbo.EmailOutbox o JOIN dbo.Reservations r ON r.Id=o.ReservationId WHERE o.ReservationId={retryId} AND o.MessageType='BookingReceived'
                """, "Retry: success on attempt 2 is the final result; both attempts and the reservation email status are recorded");
            Assert(Directory.GetFiles(mailDir, "*.txt").Count(f => File.ReadAllText(f).Contains("To: gui.lai.s209@example.com")) == 1,
                "Retry: the customer receives exactly one confirmation email");
            var retriedDetails = await Html(web.Client, $"/Reservations/Details/{retryId}");
            Assert(retriedDetails.Contains("Đã gửi thành công") && retriedDetails.Contains("2/4") && retriedDetails.Contains("(ở lần thử thứ 2)")
                && retriedDetails.Contains("1. Lần gửi đầu") && retriedDetails.Contains(FirstAttemptError) && retriedDetails.Contains("2. Lần gửi lại 1")
                && retriedDetails.Contains("data-attempt-number=\"2\" data-attempt-result=\"Thành công\"") && retriedDetails.Contains("Email: đã gửi (ở lần thử thứ 2)"),
                "Retry: staff see 2 attempts, each attempt's result and the final result 'sent at attempt 2'");
        }
        try { Directory.Delete(mailDir, true); } catch (IOException) { }

        // 2) Gửi thất bại: máy chủ SMTP không tồn tại (cổng đóng trên máy).
        var closedPort = FreePort();
        await using (var web = await Web.Start(connection, new()
        {
            ["Email__Host"] = "127.0.0.1", ["Email__Port"] = closedPort.ToString(), ["Email__EnableSsl"] = "false",
            ["Email__FromAddress"] = "no-reply@example.com", ["Email__RetryPollSeconds"] = "1"
        }))
        {
            await Login(web.Client, "manager", password);
            var before = await Scalar(connection, "SELECT COUNT(*) FROM dbo.Reservations");
            var (code, page) = await Book(web.Client, "Khách S2-09 lỗi email", "0912090903", 3, slots[2].Local, "loi.s209@example.com");
            Assert(await Scalar(connection, "SELECT COUNT(*) FROM dbo.Reservations") == before + 1, "Booking is saved even though the email fails");
            Assert(page.Contains($"data-booking-code=\"{code}\">{code}</p>") && page.Contains("data-email-outcome=\"Failed\"")
                && page.Contains("Email xác nhận chưa gửi được") && page.Contains("vẫn được ghi nhận"),
                "Confirmation page keeps the full booking code and explains the email was not sent");
            await Check(connection, $"""
                SELECT CASE WHEN o.Status='Pending' AND o.AttemptCount=1 AND o.SentAt IS NULL AND o.LastAttemptAt IS NOT NULL AND LEN(o.LastError)>0
                 THEN 1 ELSE 0 END
                FROM dbo.Reservations r JOIN dbo.EmailOutbox o ON o.ReservationId=r.Id AND o.MessageType='BookingReceived' WHERE r.Code='{code}'
                """, "Failed attempt and its error are stored for the reservation");
            var id = await Scalar(connection, $"SELECT Id FROM dbo.Reservations WHERE Code='{code}'");
            var details = await Html(web.Client, $"/Reservations/Details/{id}");
            Assert(details.Contains("Email xác nhận") && details.Contains("1/4") && details.Contains("Lỗi lần thử trước") && details.Contains("Chờ xác nhận"),
                "Staff sees the failed attempt and its error, booking status unchanged");

            // S2-09 Task 2: tự gửi lại tối đa 3 lần, cách nhau 5 phút; cả 3 lần đều lỗi → kết quả cuối cùng là thất bại.
            var emailId = await Scalar(connection, $"SELECT Id FROM dbo.EmailOutbox WHERE ReservationId={id} AND MessageType='BookingReceived'");
            string Waiting(int attempt) => $"""
                SELECT CASE WHEN o.Status='Pending' AND o.AttemptCount={attempt} AND a.Status='Failed' AND LEN(a.Error)>0 AND a.CompletedAt IS NOT NULL
                 AND DATEDIFF(second,a.CompletedAt,o.NextAttemptAt)=300
                 AND r.ConfirmationEmailStatus='Retrying' AND r.ConfirmationEmailAttempts={attempt} THEN 1 ELSE 0 END
                FROM dbo.EmailOutbox o JOIN dbo.EmailAttempts a ON a.EmailId=o.Id AND a.AttemptNumber={attempt}
                JOIN dbo.Reservations r ON r.Id=o.ReservationId WHERE o.Id={emailId}
                """;
            await Check(connection, Waiting(1), "Retry: after the failed first attempt the next try is scheduled exactly 5 minutes later");
            await Task.Delay(3000);
            await Check(connection, $"SELECT CASE WHEN AttemptCount=1 AND Status='Pending' THEN 1 ELSE 0 END FROM dbo.EmailOutbox WHERE Id={emailId}",
                "Retry: the worker does not send again before the 5 minutes are up");
            var waitingPage = await Html(web.Client, "/Reservations/Success");
            Assert(waitingPage.Contains($"data-booking-code=\"{code}\">{code}</p>") && waitingPage.Contains("Hệ thống sẽ tự gửi lại lúc")
                && waitingPage.Contains("đã thử 1/4 lần") && waitingPage.Contains("data-email-final=\"false\""),
                "Customer: confirmation page says the email will be sent again automatically");
            for (var attempt = 2; attempt <= 4; attempt++)
            {
                // Tua nhanh 5 phút chờ.
                await DatabaseTool.Execute(connection, $"UPDATE dbo.EmailOutbox SET NextAttemptAt=SYSUTCDATETIME() WHERE Id={emailId};");
                await WaitFor(connection, $"SELECT CASE WHEN AttemptCount={attempt} AND Status IN ('Pending','Failed') THEN 1 ELSE 0 END FROM dbo.EmailOutbox WHERE Id={emailId}",
                    $"Retry: the worker sends retry {attempt - 1} of 3 (attempt {attempt})");
                if (attempt < 4) await Check(connection, Waiting(attempt), $"Retry: attempt {attempt} failed and the next try is 5 minutes later");
            }
            await Check(connection, $"""
                SELECT CASE WHEN o.Status='Failed' AND o.AttemptCount=4 AND o.SentAt IS NULL AND LEN(o.LastError)>0
                 AND (SELECT COUNT(*) FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id AND a.Status='Failed' AND a.CompletedAt IS NOT NULL)=4
                 AND r.ConfirmationEmailStatus='Failed' AND r.ConfirmationEmailAttempts=4 AND r.Status='Pending' THEN 1 ELSE 0 END
                FROM dbo.EmailOutbox o JOIN dbo.Reservations r ON r.Id=o.ReservationId WHERE o.Id={emailId}
                """, "Retry: after the first attempt and 3 failed retries the final result 'failed' is recorded (booking unchanged)");
            await DatabaseTool.Execute(connection, $"UPDATE dbo.EmailOutbox SET NextAttemptAt=SYSUTCDATETIME() WHERE Id={emailId};");
            await Task.Delay(3000);
            await Check(connection, $"""
                SELECT CASE WHEN o.AttemptCount=4 AND o.Status='Failed' AND (SELECT COUNT(*) FROM dbo.EmailAttempts a WHERE a.EmailId=o.Id)=4 THEN 1 ELSE 0 END
                FROM dbo.EmailOutbox o WHERE o.Id={emailId}
                """, "Retry: the system never tries more than 3 retries");

            var failedDetails = await Html(web.Client, $"/Reservations/Details/{id}");
            Assert(failedDetails.Contains("Gửi thất bại") && failedDetails.Contains("4/4") && failedDetails.Contains("Thất bại sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)")
                && failedDetails.Contains("4. Lần gửi lại 3") && failedDetails.Contains("data-attempt-number=\"4\" data-attempt-result=\"Thất bại\"")
                && failedDetails.Contains("Email: gửi thất bại sau 4 lần thử") && failedDetails.Contains("data-email-final-result=\"true\""),
                "Staff see 4 attempts, each attempt's error and the final result 'failed'");
            Assert((await Html(web.Client, "/Reservations")).Contains("Email: gửi thất bại sau 4 lần thử"), "Reservation list shows the final email result");
            var finalPage = await Html(web.Client, "/Reservations/Success");
            Assert(finalPage.Contains("Không gửi được email xác nhận") && finalPage.Contains("sau 4 lần thử") && finalPage.Contains($"mã đặt bàn {code}")
                && finalPage.Contains("data-email-final=\"true\""), "Customer: confirmation page shows the final result when the email could not be sent");
            using (var statusJson = await web.Client.GetAsync("/Reservations/BookingEmailStatus"))
            {
                var body = await statusJson.Content.ReadAsStringAsync();
                Assert(statusJson.StatusCode == HttpStatusCode.OK && body.Contains("\"state\":\"Failed\"") && body.Contains("\"final\":true"),
                    "Customer: email status endpoint reports the final result");
            }
        }
        Console.WriteLine("PASS: S2-09 Task 1 booking confirmation email checks.");
    }

    /// <summary>Tìm các khung giờ hợp lệ (giờ mở cửa, không phải ngày nghỉ) trong tương lai bằng usp_ValidateBookingSchedule.</summary>
    internal static async Task<List<(DateTime Local, DateTime Utc)>> FreeSlots(string connection, int count)
    {
        var found = new List<(DateTime, DateTime)>();
        var today = DateTime.UtcNow.AddHours(7).Date;
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        for (var day = 20; day < 80 && found.Count < count; day++)
            foreach (var hour in new[] { 11, 12, 18, 10, 13, 17, 19 })
            {
                var local = today.AddDays(day).AddHours(hour);
                await using var cmd = new SqlCommand("dbo.usp_ValidateBookingSchedule", cn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@StartsAt", SqlDbType.DateTime2).Value = local.AddHours(-7);
                try { await cmd.ExecuteNonQueryAsync(); found.Add((local, local.AddHours(-7))); break; }
                catch (SqlException ex) when (ex.Number is >= 51000 and < 51500) { }
            }
        Assert(found.Count == count, "Found free booking slots inside opening hours");
        return found;
    }

    internal static async Task<(string Code, string Page)> Book(HttpClient client, string name, string phone, int guests, DateTime local, string? email, int? tableId = null)
    {
        using var response = await Post(client, name, phone, guests, local, email, tableId);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert(response.StatusCode == HttpStatusCode.Redirect && Location(response).StartsWith("/Reservations/Success"),
            $"Booking for {name} accepted" + (response.StatusCode == HttpStatusCode.OK ? ": " + Regex.Match(body, "validation-summary[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value : ""));
        var page = await Html(client, "/Reservations/Success");
        var code = Regex.Match(page, "data-booking-code=\"([^\"]+)\"").Groups[1].Value;
        Assert(code.Length > 0, $"Booking code {code} shown after booking");
        return (code, page);
    }

    /// <summary>Gửi form đặt bàn và trả về trang lỗi (dùng khi đặt bàn bị từ chối).</summary>
    private static async Task<string> TryBook(HttpClient client, string name, string phone, int guests, DateTime local, string? email, int? tableId)
    {
        using var response = await Post(client, name, phone, guests, local, email, tableId);
        Assert(response.StatusCode == HttpStatusCode.OK, $"Booking for {name} is rejected with the form shown again");
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    internal static async Task<(int Id, string Code)> FirstFreeTable(HttpClient client, DateTime local, int guests)
    {
        using var json = System.Text.Json.JsonDocument.Parse(await TablesJson(client, local, guests));
        var first = json.RootElement.GetProperty("tables").EnumerateArray().First();
        return (first.GetProperty("id").GetInt32(), first.GetProperty("code").GetString()!);
    }

    private static async Task<int[]> FreeTables(HttpClient client, DateTime local, int guests)
    {
        using var json = System.Text.Json.JsonDocument.Parse(await TablesJson(client, local, guests));
        return json.RootElement.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("id").GetInt32()).ToArray();
    }

    private static async Task<string> TablesJson(HttpClient client, DateTime local, int guests)
    {
        var at = Uri.EscapeDataString(local.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture));
        using var response = await client.GetAsync($"/Reservations/AvailableTables?startsAt={at}&guestCount={guests}");
        Assert(response.StatusCode == HttpStatusCode.OK, "Free tables can be loaded for the booking form");
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string name, string phone, int guests, DateTime local, string? email, int? tableId)
    {
        var form = await Html(client, "/Reservations/Create");
        var values = new List<(string, string)>
        {
            ("CustomerName", name), ("Phone", phone), ("GuestCount", guests.ToString()),
            ("StartsAt", local.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture)),
            ("__RequestVerificationToken", Token(form))
        };
        if (email is not null) values.Add(("Email", email));
        if (tableId is not null) values.Add(("TableId", tableId.Value.ToString()));
        return await client.PostAsync("/Reservations/Create", Form(values.ToArray()));
    }

    /// <summary>Định dạng ngày giờ giống email: "11:00, Thứ Hai ngày 26/10/2026".</summary>
    internal static string BookingWhen(DateTime local)
    {
        string[] weekdays = ["Chủ nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy"];
        return $"{local:HH:mm}, {weekdays[(int)local.DayOfWeek]} ngày {local.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal static async Task Login(HttpClient client, string user, string password)
    {
        var html = await client.GetStringAsync("/Account/Login");
        using var response = await client.PostAsync("/Account/Login", Form(("Identifier", user), ("Password", password), ("__RequestVerificationToken", Token(html))));
        Assert(response.StatusCode == HttpStatusCode.Redirect, $"{user} signs in");
    }

    internal static async Task<string> Html(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert(response.StatusCode == HttpStatusCode.OK, "Opens " + path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Mã nội bộ (Reservations.Code) của lượt đặt mới nhất theo số điện thoại; chỉ dùng để tra trong database.</summary>
    internal static async Task<string> InternalCode(string connection, string phone)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT TOP(1) Code FROM dbo.Reservations WHERE Phone=@phone ORDER BY Id DESC;", cn);
        cmd.Parameters.AddWithValue("@phone", phone);
        return Convert.ToString(await cmd.ExecuteScalarAsync())!.Trim();
    }

    internal static async Task<long> Scalar(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    internal static async Task Check(string connection, string sql, string name) => Assert(await Scalar(connection, sql) == 1, name);

    /// <summary>Chờ worker xử lý (tối đa 30 giây) cho tới khi câu SQL trả về 1.</summary>
    internal static async Task WaitFor(string connection, string sql, string name, int seconds = 30)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (await Scalar(connection, sql) == 1) { Assert(true, name); return; }
            await Task.Delay(250);
        }
        Assert(false, name + $" (no result within {seconds} s)");
    }

    internal static string Location(HttpResponseMessage response) => response.Headers.Location?.OriginalString ?? "";
    internal static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    internal static FormUrlEncodedContent Form(params (string Name, string Value)[] pairs) => new(pairs.Select(p => new KeyValuePair<string, string>(p.Name, p.Value)));

    internal static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    /// <summary>Một tiến trình web riêng cho kiểm thử, với cấu hình email riêng.</summary>
    internal sealed class Web : IAsyncDisposable
    {
        private Process _process = null!;
        private Task _drain = Task.CompletedTask;
        private HttpClientHandler _handler = null!;
        public HttpClient Client { get; private set; } = null!;

        public static async Task<Web> Start(string connection, Dictionary<string, string> environment)
        {
            var port = FreePort();
            var webRoot = Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web");
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = webRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(webRoot, "bin", "Debug", "net10.0", "RestaurantManagement.Web.dll"));
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
            start.Environment["RM_CONNECTION_STRING"] = connection;
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["EmailVerification__Enabled"] = "false";
            foreach (var (key, value) in environment) start.Environment[key] = value;
            var web = new Web { _process = Process.Start(start)! };
            web._drain = Task.WhenAll(web._process.StandardOutput.ReadToEndAsync(), web._process.StandardError.ReadToEndAsync());
            web._handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            web.Client = new HttpClient(web._handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(70) };
            for (var i = 0; i < 80; i++)
            {
                try { using var r = await web.Client.GetAsync("/Account/Login"); if (r.IsSuccessStatusCode) return web; } catch (HttpRequestException) { }
                if (web._process.HasExited) throw new Exception("Web process exited before startup.");
                await Task.Delay(250);
            }
            throw new Exception("Web did not start.");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            _handler.Dispose();
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
            await _drain;
            _process.Dispose();
        }
    }
}
