using RestaurantManagement.Web.Models.Reservations;

/// <summary>
/// S3-09 Task 2 (không cần database): gia hạn giữ bàn 15 phút một lần — nhãn thời hạn, nút gia hạn chỉ khi chưa gia hạn,
/// lượt đang gia hạn không bị cảnh báo trên danh sách đặt bàn, thông báo kết quả và các quy tắc trong migration 054.
/// Phần chạy thật trên SQL Server: verify-hold-extension (hoặc verify-late-reservations, verify).
/// </summary>
internal static class HoldExtensionTests
{
    internal static void Run(Action<bool, string> check)
    {
        // 11:00 UTC = 18:00 giờ Việt Nam.
        var now = new DateTime(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc);
        var holdUntil = now.AddMinutes(LateReservationRules.HoldExtensionMinutes);

        check(LateReservationRules.HoldExtensionMinutes == 15 && LateReservationRules.MaxHoldExtensions == 1,
            "S3-09 T2 rule: one extension of 15 minutes at most");
        check(LateReservationRules.HoldLabel(holdUntil) == "Gia hạn giữ bàn tới 18:15",
            "S3-09 T2 label: the new hold time is shown in Vietnam time");

        // Nút "Gia hạn 15 phút" chỉ khi chưa gia hạn; sau khi hết hạn chỉ còn ghi chú đã gia hạn.
        var fresh = new LateReservationAlert(1, "LATE01", "Khách", "090****001", 2, "A01", now.AddMinutes(-16), now.AddMinutes(-1), 16, "Confirmed");
        check(fresh.CanExtend && fresh.ExtensionLabel is null, "S3-09 T2: a warned booking that was never extended can be extended");
        var expired = fresh with { ExtensionCount = 1, HoldExtendedUntilUtc = now.AddMinutes(-2), LateMinutes = 33 };
        check(!expired.CanExtend && expired.ExtensionLabel == "Đã gia hạn 1 lần, hết hạn lúc 17:58",
            "S3-09 T2: after the extension ends the booking cannot be extended again and says when it ended");
        check(LateReservationRules.ExpiredExtensionLabel(1, null) == "Đã gia hạn 1 lần" && LateReservationRules.ExpiredExtensionLabel(0, now) is null,
            "S3-09 T2: the expired-extension note appears only after one extension");

        // Lượt đang gia hạn.
        var hold = new ExtendedReservationHold(2, "HOLD02", "Đang chờ", "090****002", 4, "A02", now.AddMinutes(-20), holdUntil, "Confirmed");
        check(hold.HoldUntilTime == "18:15" && hold.HoldLabel == "Gia hạn giữ bàn tới 18:15" && hold.AppointmentTime == "17:40",
            "S3-09 T2: an extended booking carries its appointment and new hold time");

        // Danh sách đặt bàn: trễ → đang gia hạn → sắp đến → còn lại; lượt đang gia hạn không mang cảnh báo.
        var page = new DailyReservationPage(new DateOnly(2026, 10, 12), new[]
        {
            new DailyReservationItem("SOON03", "Sắp tới", "090****003", 2, "18:10–19:40", "A03", "Đã xác nhận", IsUpcoming: true, Id: 3),
            new DailyReservationItem("HOLD02", "Đang chờ", "090****002", 4, "17:40–19:10", "A02", "Đã xác nhận", Id: 2),
            new DailyReservationItem("LATE01", "Khách", "090****001", 2, "17:44–19:14", "A01", "Đã xác nhận", Id: 1),
            new DailyReservationItem("EXPD04", "Hết hạn", "090****004", 2, "17:27–18:57", "A04", "Đã xác nhận", Id: 4),
            new DailyReservationItem("ARRV05", "Đã tới", "090****005", 2, "17:30–19:00", "A05", "Khách đã tới", Id: 5)
        }, now);
        var snapshot = new LateReservationSnapshot(now, new[]
        {
            fresh,
            expired with { Id = 4, Code = "EXPD04", TableCode = "A04" }
        }, holdUntil) { Holds = new[] { hold } };
        var merged = DailyReservationRules.WithLateAlerts(page, snapshot);
        check(merged.Reservations.Select(r => r.Code).SequenceEqual(new[] { "LATE01", "EXPD04", "HOLD02", "SOON03", "ARRV05" }),
            "S3-09 T2 list: late bookings first, then extended holds, then upcoming and the rest");
        var extendedRow = merged.Reservations.Single(r => r.Code == "HOLD02");
        check(extendedRow is { IsExtended: true, IsLate: false, IsUpcoming: false, HoldLabel: "Gia hạn giữ bàn tới 18:15" },
            "S3-09 T2 list: an extended booking shows its new hold time and no warning");
        check(merged.Reservations.Single(r => r.Code == "LATE01") is { IsLate: true, CanExtend: true, ExtensionLabel: null }
              && merged.Reservations.Single(r => r.Code == "EXPD04") is { IsLate: true, CanExtend: false, ExtensionLabel: "Đã gia hạn 1 lần, hết hạn lúc 17:58" },
            "S3-09 T2 list: only a booking never extended offers the extension; an expired one only allows no-show");
        check(merged.LateCount == 2 && !merged.Reservations.Single(r => r.Code == "ARRV05").IsExtended,
            "S3-09 T2 list: the late count excludes extended holds and an arrived guest is untouched");
        check(new LateReservationSnapshot(now, Array.Empty<LateReservationAlert>(), null).Holds.Count == 0,
            "S3-09 T2: a snapshot without holds has an empty hold list");

        // Thông báo sau khi gia hạn.
        var message = new HoldExtensionResult("LATE01", "A01", holdUntil, 1).Message;
        check(message.Contains("Đã gia hạn giữ bàn A01") && message.Contains("LATE01") && message.Contains("tới 18:15") && message.Contains("cảnh báo sẽ hiện lại"),
            "S3-09 T2 message: the waiter sees the table, the new hold time and what happens when it ends");
        check(new HoldExtensionResult("PEND05", null, holdUntil, 1).Message.StartsWith("Đã gia hạn giữ chỗ cho lượt PEND05"),
            "S3-09 T2 message: a booking without a table is extended without naming a table");

        // Migration 054: kiểm tra trạng thái + số lần gia hạn trong cùng giao dịch có khoá, thời hạn = lúc bấm + 15 phút.
        var migration = File.ReadAllText(Path.Combine(RestaurantManagement.DbTool.DatabaseTool.Root, "database", "migrations", "054_S309ExtendReservationHold.sql"));
        check(migration.Contains("EXEC dbo.usp_LockOperations") && migration.Contains("IF @extensions >= 1")
              && migration.Contains("AND ExtensionCount = 0;") && migration.Contains("THROW 51125"),
            "S3-09 T2 SQL: repeated or simultaneous extensions are serialised and only the first one is stored");
        check(migration.Contains("SET @holdUntil = DATEADD(minute, 15, @now);"),
            "S3-09 T2 SQL: the new hold ends 15 minutes after the extension (rule to confirm with the PO)");
        check(migration.Contains("THROW 51121") && migration.Contains("THROW 51124") && migration.Contains("THROW 51123"),
            "S3-09 T2 SQL: arrived, no-show and not-yet-late bookings cannot be extended");
        check(migration.Contains("w.ExtensionCount >= 1 AND w.HoldExtendedUntil > @NowUtc"),
            "S3-09 T2 SQL: a booking is listed as extended only until its new hold time");
        check(LateReservationRules.DefaultGraceMinutes == 15, "S3-09 T2: the first warning still starts at the 15-minute mark (AC 1 kept)");
    }
}
