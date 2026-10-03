using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.ViewModels;

namespace RestaurantManagement.Web.Controllers;

[AllowAnonymous]
[ResponseCache(
    Location = ResponseCacheLocation.None,
    NoStore = true)]
public class ReservationsController : Controller
{
    private readonly ReservationStore _store;
    private readonly AuditLogService _auditLogService;

    private const string PairError =
        "Không tìm thấy đặt bàn khớp với mã và số điện thoại " +
        "bạn cung cấp. Vui lòng kiểm tra lại.";

    public ReservationsController(
        ReservationStore store,
        AuditLogService auditLogService)
    {
        _store = store;
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(
            new ReservationLookupViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lookup(
        [Bind("Code,Phone")]
        ReservationLookupViewModel model)
    {
        if (!ModelState.IsValid)
            return View("Index", model);

        model.Result =
            await _store.LookupAsync(
                model.Code,
                model.Phone);

        if (model.Result is null)
        {
            ModelState.AddModelError(
                "",
                PairError);
        }

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        [Bind("Code,Phone")]
        ReservationLookupViewModel model)
    {
        if (!ModelState.IsValid)
            return View("Index", model);

        // Kiểm tra lại cặp mã và số điện thoại.
        model.Result =
            await _store.LookupAsync(
                model.Code,
                model.Phone);

        if (model.Result is null)
        {
            ModelState.AddModelError(
                "",
                PairError);

            return View("Index", model);
        }

        // Huỷ lần thứ hai không thay đổi dữ liệu
        // và không tạo thêm email.
        if (model.Result.Status == "Cancelled")
        {
            model.SuccessMessage =
                "Đặt bàn này đã được huỷ trước đó.";

            await SetEmailDeliveryMessageAsync(
                model);

            return View("Index", model);
        }

        if (!model.Result.CanCancel)
            return View("Index", model);

        try
        {
            // SQL tự kiểm tra lại trạng thái,
            // thời gian, huỷ đặt bàn,
            // ghi ReservationEvents,
            // giải phóng bàn và queue email.
            await _store.CancelAsync(
                model.Code,
                model.Phone);
        }
        catch (SqlException ex)
            when (ex.Number == 51012)
        {
            model.Result = null;

            ModelState.AddModelError(
                "",
                PairError);

            return View("Index", model);
        }
        catch (SqlException ex)
            when (ex.Number == 51013)
        {
            model.Result =
                await _store.LookupAsync(
                    model.Code,
                    model.Phone);

            if (model.Result is null)
            {
                ModelState.AddModelError(
                    "",
                    PairError);
            }
            else
            {
                model.Result.CanCancel = false;

                model.Result.CancellationMessage ??=
                    "Không thể huỷ đặt bàn lúc này. " +
                    "Vui lòng gọi trực tiếp cho quán " +
                    "để được hỗ trợ.";
            }

            return View("Index", model);
        }

        // Ghi Audit Log sau khi huỷ thành công.
        var username =
            User.Identity?.IsAuthenticated == true
                ? User.Identity.Name
                    ?? "AuthenticatedUser"
                : "Anonymous";

        var role =
            User.Identity?.IsAuthenticated == true
                ? "User"
                : "Guest";

        var ipAddress =
            HttpContext.Connection
                .RemoteIpAddress?
                .ToString()
            ?? "Unknown";

        await _auditLogService.LogAsync(
            username,
            role,
            $"Cancel reservation {model.Code}",
            ipAddress);

        // Đọc lại trạng thái mới nhất.
        model.Result =
            await _store.LookupAsync(
                model.Code,
                model.Phone);

        model.SuccessMessage =
            "Huỷ đặt bàn thành công.";

        await SetEmailDeliveryMessageAsync(
            model);

        return View("Index", model);
    }

    private async Task SetEmailDeliveryMessageAsync(
        ReservationLookupViewModel model)
    {
        if (model.Result is null)
            return;

        // Đặt bàn không có email:
        // huỷ vẫn thành công và không báo lỗi.
        if (string.IsNullOrWhiteSpace(
                model.Result.Email))
        {
            model.Result.EmailDeliveryMessage =
                null;

            return;
        }

        var status =
            await _store
                .GetCancellationEmailStatusAsync(
                    model.Code,
                    model.Phone);

        model.Result.EmailDeliveryMessage =
            status switch
            {
                "Sent" =>
                    $"Đã gửi email xác nhận đến " +
                    $"{model.Result.MaskedEmail}.",

                "Pending" or "Processing" =>
                    $"Email xác nhận đang được gửi đến " +
                    $"{model.Result.MaskedEmail}.",

                "Failed" =>
                    "Huỷ đặt bàn đã thành công, " +
                    "nhưng email xác nhận chưa gửi được.",

                "Cancelled" =>
                    null,

                _ =>
                    null
            };
    }
}