using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Cashier;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = AppRoles.Cashiers)]
[Route("Cashier/ShiftClosing")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class ShiftClosingController(
    ShiftClosingStore store,
    ShiftClosingPdfBuilder pdfBuilder,
    ILogger<ShiftClosingController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        long? shiftId,
        long? eventId,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid
            || shiftId is <= 0
            || eventId is <= 0
            || (eventId.HasValue && !shiftId.HasValue))
        {
            return BadRequest("Mã ca hoặc mã bản chốt không hợp lệ.");
        }

        try
        {
            var selectedShiftId = shiftId
                ?? await store.GetLatestClosedShiftIdAsync(
                    cancellationToken);

            if (selectedShiftId is null)
            {
                return View(new ShiftClosingPdfViewModel
                {
                    ErrorMessage = "Chưa có bản chốt ca nào."
                });
            }

            var model = await store.GetAsync(
                selectedShiftId.Value,
                eventId,
                cancellationToken);

            if (model is null)
            {
                return NotFound(
                    "Không tìm thấy ca hoặc bản chốt được chọn.");
            }

            return View(model);
        }
        catch (SqlException exception)
        {
            logger.LogError(
                exception,
                "Không đọc được bản chốt của ca {ShiftId}.",
                shiftId);

            Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;

            return View(new ShiftClosingPdfViewModel
            {
                ShiftId = shiftId ?? 0,
                ErrorMessage =
                    "Không tải được bản chốt ca. "
                    + "Vui lòng tải lại trang để thử lại."
            });
        }
    }

    [HttpGet("Download")]
    public async Task<IActionResult> Download(
        long shiftId,
        long eventId,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || shiftId <= 0 || eventId <= 0)
        {
            return BadRequest("Mã ca hoặc mã bản chốt không hợp lệ.");
        }

        try
        {
            // Đọc lại bản chốt và kiểm tra đúng ca trước khi xuất.
            var model = await store.GetAsync(
                shiftId,
                eventId,
                cancellationToken);

            if (model is null)
            {
                return NotFound("Không tìm thấy bản chốt được chọn.");
            }

            if (model.SelectedClosing is null)
            {
                TempData["Error"] = model.ErrorMessage
                    ?? "Bản chốt chưa đủ dữ liệu để xuất PDF.";

                return RedirectToAction(
                    nameof(Index),
                    new { shiftId, eventId });
            }

            cancellationToken.ThrowIfCancellationRequested();

            var pdf = pdfBuilder.Build(model);
            var closing = model.SelectedClosing;

            var fileName =
                $"chot-ca-{closing.ShiftId}"
                + $"-lan-{closing.ClosingNumber}.pdf";

            return File(pdf, "application/pdf", fileName);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Không xuất được PDF bản chốt {EventId} "
                + "của ca {ShiftId}.",
                eventId,
                shiftId);

            TempData["Error"] =
                "Không tạo được PDF. Vui lòng thử lại. "
                + "Nếu lỗi tiếp tục, hãy báo quản lý.";

            return RedirectToAction(
                nameof(Index),
                new { shiftId, eventId });
        }
    }
}