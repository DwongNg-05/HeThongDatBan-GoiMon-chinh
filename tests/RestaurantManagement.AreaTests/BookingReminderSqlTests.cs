using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.EmailVerification;

/// <summary>
/// S4-08 Task 3 (SQL Server thật): mỗi lượt đặt bàn chỉ nhận tối đa một email nhắc.
/// Tạo database tạm S408Test_*, chạy toàn bộ migration, dùng đúng SqlBookingEmailOutbox của web, rồi xoá database tạm.
/// Chạy: $env:RM_CONNECTION_STRING='Server=...;Trusted_Connection=True;TrustServerCertificate=True'
///       dotnet run --project tests/RestaurantManagement.AreaTests -- --reminder-sql
/// </summary>
internal static class BookingReminderSqlTests
{
    private sealed class CountingSender(TimeSpan delay) : IEmailSender
    {
        private readonly object _gate = new();
        private readonly List<string> _to = [];
        public int FailuresLeft { get; set; }

        public IReadOnlyList<string> To { get { lock (_gate) return [.. _to]; } }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            lock (_gate)
            {
                if (FailuresLeft > 0) { FailuresLeft--; throw new InvalidOperationException("SMTP 421: máy chủ bận (kiểm thử)"); }
                _to.Add(message.To);
            }
        }
    }

    internal static async Task Run(Action<bool, string> check)
    {
        var configured = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Đặt RM_CONNECTION_STRING trước khi chạy --reminder-sql.");
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "S408Test_" + Guid.NewGuid().ToString("N") };
        var database = builder.InitialCatalog;
        var connection = builder.ConnectionString;
        try
        {
            await DatabaseTool.Migrate(connection);
            await Exec(connection, """
                INSERT dbo.Areas(Name) VALUES(N'Khu kiểm thử S408');
                DECLARE @area int=SCOPE_IDENTITY();
                INSERT dbo.DiningTables(AreaId,Code,MaxCapacity) VALUES(@area,'T408A',4),(@area,'T408B',4),(@area,'T408C',4),(@area,'T408D',4),(@area,'T408E',4),(@area,'T408F',4),(@area,'T408G',4);
                DECLARE @now datetime2(3)=SYSUTCDATETIME();
                INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,TableId,StartsAt,EndsAt,Status,ConfirmedAt)
                SELECT CONCAT('S3R00',v.N),CONCAT(N'Khách ',v.N),CONCAT('090000002',v.N),CONCAT(N's408t3-',v.N,N'@example.com'),2,t.Id,
                       DATEADD(minute,90+v.N,@now),DATEADD(minute,180+v.N,@now),'Confirmed',@now
                FROM (VALUES(1),(2),(3),(4)) v(N) JOIN dbo.DiningTables t ON t.Code=CONCAT('T408',CHAR(64+v.N));
                """);

            // Hai "web" chạy đồng thời, mỗi web 2 lượt tác vụ, máy chủ email chậm để các lượt chạy chồng lên nhau.
            var sender = new CountingSender(TimeSpan.FromMilliseconds(150));
            BookingEmailDispatcher Web() => new(new SqlBookingEmailOutbox(connection), sender, NullLogger<BookingEmailDispatcher>.Instance);
            var webA = Web();
            var webB = Web();
            var results = await Task.WhenAll(
                Task.Run(() => webA.SendDueRemindersAsync()), Task.Run(() => webB.SendDueRemindersAsync()),
                Task.Run(() => webA.SendDueRemindersAsync()), Task.Run(() => webB.SendDueRemindersAsync()));
            var emails = Enumerable.Range(1, 4).Select(n => $"s408t3-{n}@example.com").ToArray();
            check(emails.All(e => sender.To.Count(t => t == e) == 1) && sender.To.Count == 4 && results.Sum(r => r.Sent) == 4,
                "Reminder SQL: two web servers running the task at the same time send each booking exactly one reminder");
            check(await Scalar(connection, "SELECT COUNT(*) FROM dbo.EmailOutbox WHERE MessageType='BookingReminder'") == 4
                  && await Scalar(connection, "SELECT COUNT(*) FROM dbo.Reservations WHERE Code LIKE 'S3R%' AND ReminderEmailSentAt IS NOT NULL AND ReminderEmailStatus='Sent' AND ReminderEmailAttempts=1") == 4,
                "Reminder SQL: one reminder row per booking, each marked as reminded after the successful send");

            // Chạy liên tiếp nhiều lần: không gửi thêm.
            for (var i = 0; i < 5; i++) await webA.SendDueRemindersAsync();
            check(sender.To.Count == 4, "Reminder SQL: running the task 5 more times sends nothing new");

            // Chỉ mục duy nhất: không chèn được email nhắc thứ hai cho cùng một lượt.
            var blocked = false;
            try
            {
                await Exec(connection, """
                    INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey)
                    SELECT TOP(1) Id,'BookingReminder',Email,N'x',N'{}','S408-duplicate' FROM dbo.Reservations WHERE Code='S3R001';
                    """);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { blocked = true; }
            check(blocked, "Reminder SQL: the database refuses a second reminder for the same booking");

            // Gửi thất bại: chưa đánh dấu đã nhắc, được thử lại; thành công rồi thì không gửi lại.
            await Exec(connection, """
                DECLARE @now datetime2(3)=SYSUTCDATETIME();
                INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,TableId,StartsAt,EndsAt,Status,ConfirmedAt)
                SELECT 'S3R005',N'Khách 5','0900000025',N's408t3-5@example.com',2,Id,DATEADD(minute,100,@now),DATEADD(minute,190,@now),'Confirmed',@now
                FROM dbo.DiningTables WHERE Code='T408E';
                """);
            sender.FailuresLeft = 1;
            var failed = await webA.SendDueRemindersAsync();
            check(failed is { Sent: 0, Failed: 1 }
                  && await Scalar(connection, "SELECT COUNT(*) FROM dbo.Reservations WHERE Code='S3R005' AND ReminderEmailSentAt IS NULL AND ReminderEmailStatus='Retrying' AND ReminderEmailAttempts=1") == 1,
                "Reminder SQL: a failed send is not recorded as reminded");
            await Exec(connection, "UPDATE dbo.EmailOutbox SET NextAttemptAt=SYSUTCDATETIME() WHERE MessageType='BookingReminder' AND Status='Pending';");
            var retried = await Task.WhenAll(Task.Run(() => webA.SendDueRemindersAsync()), Task.Run(() => webB.SendDueRemindersAsync()));
            check(retried.Sum(r => r.Sent) == 1 && sender.To.Count(t => t == "s408t3-5@example.com") == 1
                  && await Scalar(connection, "SELECT COUNT(*) FROM dbo.Reservations WHERE Code='S3R005' AND ReminderEmailSentAt IS NOT NULL AND ReminderEmailAttempts=2") == 1,
                "Reminder SQL: a failed reminder is retried once and succeeds");
            for (var i = 0; i < 3; i++) await webB.SendDueRemindersAsync();
            check(sender.To.Count == 5, "Reminder SQL: a booking reminded successfully is never sent again");

            await CancelFromReminder(connection, check);
        }
        finally
        {
            var master = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString;
            SqlConnection.ClearAllPools();
            await Exec(master, $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END");
        }
    }

    /// <summary>S4-08 Task 4: dbo.usp_CancelReservationFromReminder trên SQL Server thật.</summary>
    private static async Task CancelFromReminder(string connection, Action<bool, string> check)
    {
        await Exec(connection, """
            DECLARE @now datetime2(3)=SYSUTCDATETIME();
            INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,TableId,StartsAt,EndsAt,Status,ConfirmedAt)
            SELECT v.Code,N'Khách huỷ',v.Phone,N's408t4@example.com',2,t.Id,DATEADD(minute,v.M,@now),DATEADD(minute,v.M+90,@now),'Confirmed',@now
            FROM (VALUES('S4C006','0900000036','T408F',100),('S4C007','0900000037','T408G',60)) v(Code,Phone,TableCode,M)
            JOIN dbo.DiningTables t ON t.Code=v.TableCode;
            """);
        async Task<string> Cancel(long id, string code)
        {
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.usp_CancelReservationFromReminder", cn) { CommandType = System.Data.CommandType.StoredProcedure };
            cmd.Parameters.Add("@ReservationId", System.Data.SqlDbType.BigInt).Value = id;
            cmd.Parameters.Add("@Code", System.Data.SqlDbType.Char, 6).Value = code;
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        async Task<long> IdOf(string code) => await Scalar(connection, $"SELECT Id FROM dbo.Reservations WHERE Code='{code}'");
        async Task<string> Snapshot(long id)
        {
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand($"""
                SELECT CONCAT(r.Status,'|',CONVERT(varchar(30),r.CancelledAt,126),'|',
                  (SELECT COUNT(*) FROM dbo.ReservationEvents e WHERE e.ReservationId=r.Id),'|',
                  (SELECT COUNT(*) FROM dbo.EmailOutbox o WHERE o.ReservationId=r.Id AND o.MessageType='BookingCancelled'))
                FROM dbo.Reservations r WHERE r.Id={id}
                """, cn);
            return (string)(await cmd.ExecuteScalarAsync())!;
        }

        var ok = await IdOf("S4C006");
        check(await Cancel(ok, "S4C006") == "Cancelled" && (await Snapshot(ok)).StartsWith("Cancelled|", StringComparison.Ordinal)
              && (await Snapshot(ok)).EndsWith("|1|1", StringComparison.Ordinal),
            "Cancel SQL: a booking more than 60 minutes away is cancelled from the reminder link (one event, one cancellation email)");
        var after = await Snapshot(ok);
        check(await Cancel(ok, "S4C006") == "AlreadyCancelled" && await Cancel(ok, "S4C006") == "AlreadyCancelled" && await Snapshot(ok) == after,
            "Cancel SQL: repeating the cancel request changes nothing (same time, events and emails)");

        var other = await IdOf("S3R003");
        var before = await Snapshot(other);
        check(await Cancel(other, "S4C006") == "NotFound" && await Cancel(ok + 100000, "S3R003") == "NotFound" && await Snapshot(other) == before
              && before.StartsWith("Confirmed|", StringComparison.Ordinal),
            "Cancel SQL: a link only cancels its own booking (id and code must both match)");

        var tooLate = await IdOf("S4C007");
        var tooLateBefore = await Snapshot(tooLate);
        check(await Cancel(tooLate, "S4C007") == "TooLate" && await Snapshot(tooLate) == tooLateBefore,
            "Cancel SQL: a booking 60 minutes away or less is not cancelled");

        var arrived = await IdOf("S3R004");
        await Exec(connection, $"UPDATE dbo.Reservations SET Status='Arrived',ArrivedAt=SYSUTCDATETIME() WHERE Id={arrived};");
        var arrivedBefore = await Snapshot(arrived);
        check(await Cancel(arrived, "S3R004") == "Arrived" && await Snapshot(arrived) == arrivedBefore,
            "Cancel SQL: a booking whose guest has arrived is not cancelled");
    }

    private static async Task Exec(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> Scalar(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
