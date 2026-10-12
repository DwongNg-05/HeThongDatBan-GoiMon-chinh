using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;

/// <summary>
/// S3-09 Task 1 (không cần database): chuẩn hoá số điện thoại cho lịch sử không tới, nhãn trễ, thông báo sau khi đánh dấu,
/// gắn cảnh báo vào danh sách đặt bàn và các quy tắc trong migration 049. Phần chạy thật trên SQL Server: verify-late-reservations.
/// </summary>
internal static class LateReservationTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Chuẩn hoá số điện thoại: mọi cách nhập của cùng một số → một chuỗi, để lịch sử không tới gom về một nơi.
        var same = new[] { "0901234567", " 0901 234 567 ", "090.123.4567", "090-123-4567", "(090) 123 4567", "+84 901 234 567", "+84901234567", "84901234567", "0084 901 234 567" };
        check(same.All(input => ReservationPhoneNormalizer.Normalize(input) == "0901234567"),
            "S3-09 phone: spaces, dots, dashes, brackets, +84, 84 and 0084 all normalise to 0901234567");
        check(new[] { null, "", "   ", "090123456", "09012345678", "1901234567", "0901-23a-4567", "+85 901 234 567", "849012345678" }
                .All(input => ReservationPhoneNormalizer.Normalize(input) is null),
            "S3-09 phone: too short, too long, letters or a foreign code are rejected");

        // Nhãn trễ.
        check(LateReservationRules.LateLabel(15) == "Trễ 15 phút" && LateReservationRules.LateLabel(59) == "Trễ 59 phút"
              && LateReservationRules.LateLabel(60) == "Trễ 1 giờ 00 phút" && LateReservationRules.LateLabel(125) == "Trễ 2 giờ 05 phút",
            "S3-09 label: lateness is shown in minutes, then hours and minutes");
        check(LateReservationRules.DefaultGraceMinutes == 15, "S3-09 rule: the warning mark is 15 minutes after the appointment");

        // Thông báo sau khi đánh dấu.
        var freed = new NoShowMarkResult(false, "ABC123", "A01", "Available", true, 1).Message;
        check(freed.Contains("ABC123") && freed.Contains("Bàn A01 đã trống") && freed.Contains("1 lần không tới"),
            "S3-09 message: marking frees the table and reports the phone's no-show count");
        var busyTable = new NoShowMarkResult(false, "ABC124", "A02", "Serving", false, 2).Message;
        check(busyTable.Contains("Bàn A02 không đổi trạng thái") && busyTable.Contains("2 lần"),
            "S3-09 message: a table with other guests is not freed by mistake and the message says so");
        var noTable = new NoShowMarkResult(false, "ABC125", null, null, false, 1).Message;
        check(noTable.Contains("chưa xếp bàn"), "S3-09 message: a booking without a table is marked without touching any table");
        var repeated = new NoShowMarkResult(true, "ABC123", "A01", "Available", false, 1).Message;
        check(repeated.Contains("đã được đánh dấu khách không tới trước đó") && repeated.Contains("không ghi thêm") && repeated.Contains("1 lần"),
            "S3-09 message: a repeated action says it was already done and the count stays the same");

        // Gắn cảnh báo vào danh sách đặt bàn: lượt trễ lên đầu (giữ thứ tự cũ), lượt khác giữ nguyên.
        var now = new DateTime(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc);
        var page = new DailyReservationPage(new DateOnly(2026, 10, 12), new[]
        {
            new DailyReservationItem("SOON01", "Sắp tới", "090****001", 2, "18:10–19:40", "A01", "Đã xác nhận", IsUpcoming: true, Id: 1),
            new DailyReservationItem("LATE02", "Trễ hai", "090****002", 2, "17:30–19:00", "A02", "Đã xác nhận", Id: 2),
            new DailyReservationItem("ARRV03", "Đã tới", "090****003", 2, "17:00–18:30", "A03", "Khách đã tới", Id: 3),
            new DailyReservationItem("LATE04", "Trễ bốn", "090****004", 2, "17:40–19:10", "Chưa xếp bàn", "Chờ xác nhận", Id: 4)
        }, now);
        var alerts = new LateReservationSnapshot(now, new[]
        {
            new LateReservationAlert(2, "LATE02", "Trễ hai", "090****002", 2, "A02", now.AddMinutes(-30), now.AddMinutes(-15), 30, "Confirmed"),
            new LateReservationAlert(4, "LATE04", "Trễ bốn", "090****004", 2, null, now.AddMinutes(-20), now.AddMinutes(-5), 20, "Pending")
        }, now.AddMinutes(5));
        var merged = DailyReservationRules.WithLateAlerts(page, alerts);
        check(merged.Reservations.Select(r => r.Code).SequenceEqual(new[] { "LATE02", "LATE04", "SOON01", "ARRV03" }),
            "S3-09 list: late bookings move to the top, keeping the original order inside each group");
        check(merged.Reservations[0] is { IsLate: true, LateMinutes: 30, LateLabel: "Trễ 30 phút", IsUpcoming: false }
              && merged.Reservations[1].LateLabel == "Trễ 20 phút" && !merged.Reservations[3].IsLate && merged.Reservations[2].IsUpcoming,
            "S3-09 list: late rows carry how late they are; an arrived guest has no warning");
        check(merged.LateCount == 2 && merged.NextWarnAtUtc == now.AddMinutes(5) && merged.EvaluatedAtUtc == now,
            "S3-09 list: the page reports the late count and when the next booking reaches the mark");
        var none = DailyReservationRules.WithLateAlerts(page, new LateReservationSnapshot(now, Array.Empty<LateReservationAlert>(), null));
        check(none.Reservations.SequenceEqual(page.Reservations) && none.LateCount == 0, "S3-09 list: without warnings the list is unchanged");

        // Migration 049: lưu lịch sử một lần mỗi lượt, khoá thao tác, chặn khách đã tới, cảnh báo từ đúng mốc.
        var migration = File.ReadAllText(Path.Combine(RestaurantManagement.DbTool.DatabaseTool.Root, "database", "migrations", "049_S309LateReservationNoShow.sql"));
        check(migration.Contains("CONSTRAINT UQ_ReservationNoShowHistory_Reservation UNIQUE") && migration.Contains("tr_Reservations_NoShowHistory"),
            "S3-09 SQL: each booking can enter the phone no-show history only once");
        check(migration.Contains("EXEC dbo.usp_LockOperations") && migration.Contains("SET @outcome = 'AlreadyNoShow'"),
            "S3-09 SQL: concurrent or repeated actions are serialised and the repeat is not recorded again");
        check(migration.Contains("THROW 51121") && migration.Contains("r.ArrivedAt IS NULL") && migration.Contains("w.WarnAt <= @NowUtc"),
            "S3-09 SQL: arrived guests are excluded and the warning starts exactly at the mark");
    }
}
