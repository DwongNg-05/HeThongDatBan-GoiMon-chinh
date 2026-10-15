using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;

/// <summary>
/// S3-09 Task 3 (không cần database): ngưỡng cảnh báo 3 lần / 90 ngày, nội dung cảnh báo, khi nào phải xác nhận lại,
/// cách nhập tương đương của số điện thoại, khách không đăng nhập không bị tra cứu, và quy tắc mốc biên trong migration 055.
/// Phần chạy thật trên SQL Server + web: verify-phone-no-show (hoặc verify).
/// </summary>
internal static class PhoneNoShowWarningTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        check(PhoneNoShowRules.WarningThreshold == 3 && PhoneNoShowRules.WindowDays == 90,
            "S3-09 T3 rule: warn from 3 no-shows within 90 days");

        // 0, 2, 3 và hơn 3 lần.
        var last = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc);   // 19:30 ngày 01/10/2026 giờ Việt Nam
        var zero = new PhoneNoShowSummary("0901234567", 0, null);
        var two = new PhoneNoShowSummary("0901234567", 2, last);
        var three = new PhoneNoShowSummary("0901234567", 3, last);
        var five = new PhoneNoShowSummary("0901234567", 5, last);
        check(!zero.IsWarning && zero.Message is null && !two.IsWarning && two.Message is null,
            "S3-09 T3: 0 or 2 no-shows give no warning");
        check(three.IsWarning && five.IsWarning, "S3-09 T3: 3 and more than 3 no-shows give a warning");
        check(three.Message == "Số điện thoại này có 3 lần khách không tới trong 90 ngày gần nhất (gần nhất ngày 01/10/2026). Hãy trao đổi lại với khách trước khi tạo đặt bàn."
              && five.Message!.Contains("có 5 lần khách không tới"),
            "S3-09 T3: the warning shows how many no-shows and when the last one was");

        // Xác nhận: chưa đánh dấu → hỏi; đánh dấu đúng số lần → cho tạo; lịch sử tăng sau khi xác nhận → hỏi lại.
        check(PhoneNoShowRules.NeedsConfirmation(three, confirmed: false, acknowledgedCount: 3),
            "S3-09 T3: a warned phone needs the confirmation box ticked");
        check(!PhoneNoShowRules.NeedsConfirmation(three, confirmed: true, acknowledgedCount: 3),
            "S3-09 T3: after confirming the current count the booking may continue");
        check(PhoneNoShowRules.NeedsConfirmation(three, confirmed: true, acknowledgedCount: 2)
              && PhoneNoShowRules.NeedsConfirmation(three, confirmed: true, acknowledgedCount: 0),
            "S3-09 T3: if the history grew while the form was open the staff member must confirm again");
        check(!PhoneNoShowRules.NeedsConfirmation(two, confirmed: false, acknowledgedCount: 0),
            "S3-09 T3: below the threshold nothing needs confirming");

        // Cách nhập tương đương → cùng một số (cùng kết quả tra cứu).
        check(new[] { "0985309203", "0985 309 203", "098.530.9203", "+84 985 309 203", "84985309203", "0084985309203" }
                .Select(ReservationPhoneNormalizer.Normalize).Distinct().SingleOrDefault() == "0985309203",
            "S3-09 T3: equivalent ways of typing a phone number look up the same history");

        // Gửi form: số tương đương được đổi về dạng chuẩn và bỏ lỗi định dạng; số sai giữ nguyên lỗi.
        var form = new ManagedTableReservationCreateViewModel { Phone = "+84 985 309 203" };
        var state = new ModelStateDictionary();
        state.AddModelError(nameof(form.Phone), "Số điện thoại phải gồm đúng 10 chữ số và bắt đầu bằng 0.");
        PhoneNoShowGuard.NormalizePhone(state, form);
        check(form.Phone == "0985309203" && state.IsValid, "S3-09 T3: an equivalent phone number is accepted in its standard form");
        var bad = new ReservationCreateViewModel { Phone = "12345" };
        var badState = new ModelStateDictionary();
        badState.AddModelError(nameof(bad.Phone), "Số điện thoại phải gồm 10 chữ số.");
        PhoneNoShowGuard.NormalizePhone(badState, bad);
        check(bad.Phone == "12345" && !badState.IsValid, "S3-09 T3: an invalid phone number keeps its error");

        // Khách không đăng nhập (và Bếp, Thu ngân) không bị tra cứu: không có cảnh báo, không chặn, không chạm database.
        var guest = new ReservationCreateViewModel { Phone = "0985309203" };
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var kitchen = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Kitchen") }, "Test"));
        check(!await PhoneNoShowGuard.RequireConfirmationAsync(anonymous, new ModelStateDictionary(), guest, "Server=unused", CancellationToken.None)
              && !await PhoneNoShowGuard.RequireConfirmationAsync(kitchen, new ModelStateDictionary(), guest, "Server=unused", CancellationToken.None)
              && guest.PhoneWarning is null,
            "S3-09 T3: a guest booking on their own never sees another phone's no-show history");

        check(new ReservationCreateViewModel() is IPhoneNoShowForm && new ManagedTableReservationCreateViewModel() is IPhoneNoShowForm,
            "S3-09 T3: both staff booking forms carry the warning and confirmation fields");

        // Migration 055: cửa sổ 90 ngày trượt, mốc biên được tính, theo số đã chuẩn hoá.
        var migration = File.ReadAllText(Path.Combine(RestaurantManagement.DbTool.DatabaseTool.Root, "database", "migrations", "055_S309PhoneNoShowWarning.sql"));
        check(migration.Contains("h.NoShowAt >= DATEADD(day, -90, @NowUtc)") && migration.Contains("h.NoShowAt <= @NowUtc")
              && migration.Contains("FROM dbo.ReservationNoShowHistory h") && migration.Contains("WHERE h.Phone = @Phone"),
            "S3-09 T3 SQL: no-shows are counted per normalised phone in the last 90 days, the boundary included");
    }
}
