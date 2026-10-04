using System.Net;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

/// <summary>
/// S2-09 Task 3: nhân viên mở chi tiết đặt bàn và thấy trạng thái email xác nhận (đang chờ, đang thử lại,
/// đã gửi, thất bại), số lần thử, lần thử gần nhất, kết quả cuối cùng và lỗi của đúng lượt đặt bàn đó.
/// Dùng 5 lượt đặt bàn mẫu D00001–D00005 và khôi phục dữ liệu sau khi kiểm tra.
/// Client phải đang đăng nhập bằng nhân viên/quản lý.
/// </summary>
internal static class ReservationEmailStatusVerification
{
    // Raw string có nội suy ($""") không dùng {{ }} để thoát dấu ngoặc nhọn, nên đưa JSON rỗng vào qua hằng số.
    private const string EmptyJson = "{}";
    private const string RetryError = "S209-LOI-THU-LAI 421 may chu ban";
    private const string FailedError = "S209-LOI-THAT-BAI 550 hop thu khong ton tai";

    internal static async Task Run(string connection, HttpClient client)
    {
        await Check(connection, """
            SELECT CASE WHEN COL_LENGTH('dbo.EmailOutbox','LastAttemptAt') IS NOT NULL
             -- Từ migration 030, việc nhận email để gửi nằm trong usp_StartEmailAttempt (usp_ClaimEmail gọi thủ tục này).
             AND (OBJECT_DEFINITION(OBJECT_ID('dbo.usp_ClaimEmail')) LIKE '%LastAttemptAt=SYSUTCDATETIME()%'
              OR OBJECT_DEFINITION(OBJECT_ID('dbo.usp_StartEmailAttempt')) LIKE '%LastAttemptAt=SYSUTCDATETIME()%')
             AND EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_EmailOutbox_Reservation') THEN 1 ELSE 0 END
            """, "Migration 024 records the last send attempt");

        var ids = new long[5];
        for (var i = 0; i < ids.Length; i++)
            ids[i] = await Scalar<long>(connection, $"SELECT Id FROM dbo.Reservations WHERE Code='D0000{i + 1}'");
        var (sent, waiting, retrying, failed, noEmail) = (ids[0], ids[1], ids[2], ids[3], ids[4]);

        await DatabaseTool.Execute(connection, $"""
            UPDATE dbo.Reservations SET Email=CONCAT(N's209-',Code,N'@example.com') WHERE Id IN ({sent},{waiting},{retrying},{failed});
            UPDATE dbo.Reservations SET Email=NULL WHERE Id={noEmail};
            DECLARE @now datetime2(3)=SYSUTCDATETIME();
            INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey,Status,AttemptCount,NextAttemptAt,LastAttemptAt,SentAt,LastError)
            SELECT r.Id,'BookingReceived',r.Email,N'Thông tin đặt bàn',N'{EmptyJson}',CONCAT('S209:',r.Id),v.Status,v.Attempts,
                   DATEADD(year,1,@now),v.LastAttemptAt,v.SentAt,v.LastError
            FROM (VALUES
              ({sent},'Sent',1,DATEADD(minute,-10,@now),DATEADD(minute,-10,@now),NULL),
              ({waiting},'Pending',0,NULL,NULL,NULL),
              ({retrying},'Pending',2,DATEADD(minute,-3,@now),NULL,N'{RetryError}'),
              ({failed},'Failed',4,DATEADD(minute,-1,@now),NULL,N'{FailedError}')
            ) v(ReservationId,Status,Attempts,LastAttemptAt,SentAt,LastError)
            JOIN dbo.Reservations r ON r.Id=v.ReservationId;
            """);
        try
        {
            async Task<string> Details(long id)
            {
                using var response = await client.GetAsync($"/Reservations/Details/{id}");
                Assert(response.StatusCode == HttpStatusCode.OK, $"Staff opens reservation details {id}");
                return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            }

            foreach (var id in ids)
            {
                var html = await Details(id);
                Assert(html.Contains("Trạng thái đặt bàn") && html.Contains("Chờ xác nhận") && html.Contains("Email xác nhận")
                    && html.Contains($"data-reservation-id=\"{id}\""), $"Reservation {id}: booking status and email section are separate");
            }

            var sentHtml = await Details(sent);
            Assert(sentHtml.Contains("Đã gửi thành công") && sentHtml.Contains("1/4") && sentHtml.Contains("Thành công lúc")
                && sentHtml.Contains("data-final=\"true\"") && !sentHtml.Contains("S209-LOI"), "Sent email: success, 1 attempt, final result, no error");

            var waitingHtml = await Details(waiting);
            Assert(waitingHtml.Contains("Đang chờ gửi") && waitingHtml.Contains("0/4") && waitingHtml.Contains("Chưa thử gửi")
                && waitingHtml.Contains("Chưa có kết quả cuối cùng") && waitingHtml.Contains("data-final=\"false\"") && !waitingHtml.Contains("S209-LOI"),
                "Queued email: waiting, no attempts, no final result yet");

            var retryHtml = await Details(retrying);
            Assert(retryHtml.Contains("Đang thử gửi lại") && retryHtml.Contains("2/4") && retryHtml.Contains("1 lần gửi lại")
                && retryHtml.Contains("Thử lại lúc") && retryHtml.Contains(RetryError) && !retryHtml.Contains(FailedError),
                "Retrying email: retry count, next retry time and only its own error");

            var failedHtml = await Details(failed);
            Assert(failedHtml.Contains("Gửi thất bại") && failedHtml.Contains("4/4") && failedHtml.Contains("Thất bại sau 4 lần thử")
                && failedHtml.Contains(FailedError) && !failedHtml.Contains(RetryError) && failedHtml.Contains("data-final=\"true\""),
                "Failed email: failed after all attempts with only its own error");

            var noEmailHtml = await Details(noEmail);
            Assert(noEmailHtml.Contains("Khách không để lại email") && !noEmailHtml.Contains("S209-LOI"), "Reservation without email explains no email is sent");

            // Kết quả gửi mới: worker nhận và gửi thành công email đang thử lại; khu vực email cập nhật theo.
            await DatabaseTool.Execute(connection, $"UPDATE dbo.EmailOutbox SET NextAttemptAt='2000-01-01' WHERE DedupeKey='S209:{retrying}';");
            var (emailId, attempt) = await Claim(connection);
            Assert(emailId == await Scalar<long>(connection, $"SELECT Id FROM dbo.EmailOutbox WHERE DedupeKey='S209:{retrying}'") && attempt == 3,
                "Worker claims the retrying email as attempt 3");
            await Check(connection, $"SELECT CASE WHEN Status='Processing' AND LastAttemptAt>=DATEADD(minute,-1,SYSUTCDATETIME()) THEN 1 ELSE 0 END FROM dbo.EmailOutbox WHERE Id={emailId}",
                "Claim records the last attempt time");
            using (var sending = await client.GetAsync($"/Reservations/EmailStatus/{retrying}"))
            {
                var html = WebUtility.HtmlDecode(await sending.Content.ReadAsStringAsync());
                Assert(sending.StatusCode == HttpStatusCode.OK && html.Contains("Đang gửi") && html.Contains("3/4") && html.Contains("data-final=\"false\"")
                    && !html.Contains("<html"), "Refresh endpoint returns only the email section while sending");
            }
            await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CompleteEmail @EmailId={emailId},@AttemptNumber=3,@Succeeded=1;");
            using (var refreshed = await client.GetAsync($"/Reservations/EmailStatus/{retrying}"))
            {
                var html = WebUtility.HtmlDecode(await refreshed.Content.ReadAsStringAsync());
                Assert(html.Contains("Đã gửi thành công") && html.Contains("3/4") && html.Contains("2 lần gửi lại") && html.Contains("Thành công lúc")
                    && !html.Contains(RetryError) && html.Contains("data-final=\"true\""), "New send result: email section shows success and clears the old error");
            }
            Assert((await Details(retrying)).Contains("Chờ xác nhận"), "Email success does not change the reservation status");

            using (var missing = await client.GetAsync("/Reservations/EmailStatus/999999999"))
                Assert(missing.StatusCode == HttpStatusCode.NotFound, "Unknown reservation has no email section");
            Console.WriteLine("PASS: S2-09 Task 3 reservation email status checks.");
        }
        finally
        {
            await DatabaseTool.Execute(connection, $"""
                DELETE dbo.EmailOutbox WHERE DedupeKey LIKE 'S209:%';
                UPDATE dbo.Reservations SET Email=NULL WHERE Id IN ({string.Join(',', ids)});
                """);
        }
    }

    private static async Task<(long Id, int Attempt)> Claim(string connection)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_ClaimEmail", cn) { CommandType = System.Data.CommandType.StoredProcedure };
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert(await reader.ReadAsync(), "usp_ClaimEmail returns an email");
        return (reader.GetInt64(reader.GetOrdinal("Id")), reader.GetInt32(reader.GetOrdinal("AttemptCount")));
    }

    private static async Task<T> Scalar<T>(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    private static async Task Check(string connection, string sql, string name) =>
        Assert(await Scalar<int>(connection, sql) == 1, name);

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
