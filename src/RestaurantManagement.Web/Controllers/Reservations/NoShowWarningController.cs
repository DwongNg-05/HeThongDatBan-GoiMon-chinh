using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NoShowWarningController(NoShowBookingWarning warning) : Controller
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Check(string? phone, CancellationToken ct)
    {
        if (phone is null || phone.Length > 100 || ReservationPhoneNormalizer.Normalize(phone) is null)
            return BadRequest(new { message = "Vui lòng nhập số điện thoại hợp lệ." });
        var model = new NoShowWarningInput();
        await warning.Refresh(model, phone, ct);
        return Json(new { count = model.NoShowCount, token = model.NoShowWarningToken });
    }
}
