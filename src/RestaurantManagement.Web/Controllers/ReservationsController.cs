using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.ViewModels;

namespace RestaurantManagement.Web.Controllers;

[AllowAnonymous]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
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

    // Mở trang tra cứu.
    [HttpGet]
    public IActionResult Index()
    {
        return View(new ReservationLookupViewModel());
    }

    // Nhận mã và số điện thoại để tra cứu.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lookup(
        [Bind("Code,Phone")] ReservationLookupViewModel model)
    {
        if (!ModelState.IsValid)
            return View("Index", model);

        model.Result = await _store.LookupAsync(
            model.Code,
            model.Phone);

        if (model.Result is null)
            ModelState.AddModelError("", PairError);

        return View("Index", model);
    }

    // Nhận yêu cầu sau khi khách xác nhận huỷ.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        [Bind("Code,Phone")] ReservationLookupViewModel model)
    {
        if (!ModelState.IsValid)
            return View("Index", model);

        // Kiểm tra lại cặp mã và số điện thoại.
        model.Result = await _store.LookupAsync(
            model.Code,
            model.Phone);

        if (model.Result is null)
        {
            ModelState.AddModelError("", PairError);
            return View("Index", model);
        }

        // Huỷ lần thứ hai không thực hiện thay đổi thêm.
        if (model.Result.Status == "Cancelled")
        {
            model.SuccessMessage =
                "Đặt bàn này đã được huỷ trước đó.";

            return View("Index", model);
        }

        if (!model.Result.CanCancel)
            return View("Index", model);

        try
        {
            // Thủ tục SQL kiểm tra lại trạng thái và thời gian,
            // đồng thời huỷ và giải phóng bàn trong transaction.
            await _store.CancelAsync(model.Code, model.Phone);
        }
        catch (SqlException ex) when (ex.Number == 51012)
        {
            model.Result = null;
            ModelState.AddModelError("", PairError);

            return View("Index", model);
        }
        catch (SqlException ex) when (ex.Number == 51013)
        {
            // Trạng thái hoặc thời gian đã đổi sau lần đọc trước.
            model.Result = await _store.LookupAsync(
                model.Code,
                model.Phone);

            if (model.Result is null)
            {
                ModelState.AddModelError("", PairError);
            }
            else
            {
                model.Result.CanCancel = false;

                model.Result.CancellationMessage ??=
                    "Không thể huỷ đặt bàn lúc này. " +
                    "Vui lòng gọi trực tiếp cho quán để được hỗ trợ.";
            }

            return View("Index", model);
        }

        // Ghi Audit Log sau khi huỷ thành công.
        var username =
            User.Identity?.IsAuthenticated == true
                ? User.Identity.Name ?? "AuthenticatedUser"
                : "Anonymous";

        var role =
            User.Identity?.IsAuthenticated == true
                ? "User"
                : "Guest";

        var ipAddress =
            HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? "Unknown";

        await _auditLogService.LogAsync(
            username,
            role,
            $"Cancel reservation {model.Code}",
            ipAddress);

        // Đọc lại thông tin mới nhất sau khi huỷ thành công.
        model.Result = await _store.LookupAsync(
            model.Code,
            model.Phone);

        model.SuccessMessage = "Huỷ đặt bàn thành công.";

        return View("Index", model);
    }
}