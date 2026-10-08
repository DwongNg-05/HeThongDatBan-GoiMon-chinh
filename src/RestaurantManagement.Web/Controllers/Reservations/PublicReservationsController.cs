using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services.Reservations;
using System.Data;
using System.Text.Json;

namespace RestaurantManagement.Web.Controllers;

[AllowAnonymous]
[Route("PublicReservations")]
public class PublicReservationsController(IConfiguration configuration, ReservationSlotService slotService) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    [HttpGet("Create")]
    public async Task<IActionResult> Create()
    {
        var model = new ReservationCreateViewModel { GuestCount = 2 };
        await SetFirstAvailableSlot(model);
        SetReservationDateRange(model);
        await LoadActiveAreas(model);
        return View(model);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReservationCreateViewModel model)
    {
        await LoadActiveAreas(model);
        SetReservationDateRange(model);
        ReplaceGuestCountBindingError(model);
        if (!ModelState.IsValid) return View(model);
        if (model.PreferredAreaId.HasValue && !model.Areas.Any(area => area.Id == model.PreferredAreaId.Value))
        {
            ModelState.AddModelError(nameof(model.PreferredAreaId), "Khu vực bạn chọn không còn hoạt động.");
            return View(model);
        }

        var slots = await slotService.GetSlotsAsync(model.ReservationDate!.Value, model.GuestCount, model.PreferredAreaId);
        var selectedTime = model.ReservationTime!.Value.ToString("HH:mm");
        if (!slots.Slots.Contains(selectedTime))
        {
            ModelState.AddModelError(nameof(model.ReservationTime), slots.Message ?? "Khung giờ đã chọn không còn nằm trong giờ mở cửa.");
            return View(model);
        }

        try
        {
            var localTime = model.ReservationDate!.Value.ToDateTime(model.ReservationTime!.Value);
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_CreateReservation", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add(new SqlParameter("@CustomerName", SqlDbType.NVarChar, 100) { Value = model.CustomerName.Trim() });
            command.Parameters.Add(new SqlParameter("@Phone", SqlDbType.VarChar, 50) { Value = model.Phone.Trim() });
            command.Parameters.Add(new SqlParameter("@GuestCount", SqlDbType.Int) { Value = model.GuestCount });
            command.Parameters.Add(new SqlParameter("@StartsAt", SqlDbType.DateTime2) { Value = VietnamTime.ToUtc(localTime) });
            command.Parameters.Add(new SqlParameter("@PreferredAreaId", SqlDbType.Int) { Value = (object?)model.PreferredAreaId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, 500)
            {
                Value = string.IsNullOrWhiteSpace(model.Notes) ? DBNull.Value : model.Notes.Trim()
            });

            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Không nhận được mã đặt bàn.");
            TempData["ReservationConfirmation"] = JsonSerializer.Serialize(new ReservationConfirmationViewModel
            {
                Code = reader.GetString(reader.GetOrdinal("Code")),
                CustomerName = model.CustomerName.Trim(),
                Phone = model.Phone.Trim(),
                GuestCount = model.GuestCount,
                ReservationDate = model.ReservationDate.Value,
                ReservationTime = model.ReservationTime.Value,
                AreaName = model.PreferredAreaId.HasValue ? model.Areas.Single(area => area.Id == model.PreferredAreaId.Value).Name : "Không yêu cầu",
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim()
            });
            return RedirectToAction(nameof(Success));
        }
        catch (SqlException ex) when (ex.Number == 51005)
        {
            ModelState.AddModelError(nameof(model.Phone), PendingReservationLimit.ReachedMessage);
            return View(model);
        }
        catch (SqlException ex) when (ex.Number == 51006)
        {
            ModelState.AddModelError(nameof(model.ReservationTime), ex.Message);
            return View(model);
        }
        catch (SqlException ex) when (ex.Number is 51003 or 51004 or 51410 or 51411 or 51412 or 51413 or 51414)
        {
            ModelState.AddModelError(nameof(model.ReservationTime), ex.Message);
            return View(model);
        }
        catch (Exception)
        {
            await LoadActiveAreas(model);
            ModelState.AddModelError(string.Empty, "Hệ thống chưa thể lưu đơn đặt bàn. Vui lòng thử lại.");
            return View(model);
        }
    }

    [HttpGet("Success")]
    public IActionResult Success()
    {
        var json = TempData["ReservationConfirmation"] as string;
        if (string.IsNullOrWhiteSpace(json)) return RedirectToAction(nameof(Create));
        var confirmation = JsonSerializer.Deserialize<ReservationConfirmationViewModel>(json);
        return confirmation is null ? RedirectToAction(nameof(Create)) : View(confirmation);
    }

    [HttpGet("/api/reservation-areas")]
    public async Task<IActionResult> Areas()
    {
        var model = new ReservationCreateViewModel();
        await LoadActiveAreas(model);
        return Ok(model.Areas.Select(area => new { area.Id, area.Name }));
    }

    [HttpGet("/api/reservation-slots")]
    public async Task<IActionResult> Slots(DateOnly? date, int? guestCount, int? preferredAreaId)
    {
        if (date is null) return BadRequest(new { message = "Vui lòng chọn ngày đặt bàn." });
        return Ok(await slotService.GetSlotsAsync(date.Value, guestCount, preferredAreaId));
    }

    private async Task LoadActiveAreas(ReservationCreateViewModel model)
    {
        model.Areas.Clear();
        const string sql = "SELECT Id, Name, SortOrder FROM dbo.Areas WHERE IsActive=1 ORDER BY SortOrder, Name, Id;";
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            model.Areas.Add(new BookingAreaOption
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder"))
            });
    }

    private void ReplaceGuestCountBindingError(ReservationCreateViewModel model)
    {
        var field = nameof(model.GuestCount);
        if (!ModelState.TryGetValue(field, out var state) || !state.Errors.Any(error => error.Exception is not null)) return;
        state.Errors.Clear();
        state.Errors.Add("Số khách phải là số nguyên từ 1 đến 20. Đoàn trên 20 khách, vui lòng liên hệ trực tiếp nhà hàng.");
    }

    private static void SetReservationDateRange(ReservationCreateViewModel model)
    {
        model.MinimumReservationDate = ReservationSchedulePolicy.Today;
        model.MaximumReservationDate = ReservationSchedulePolicy.LastReservableDate;
    }

    private async Task SetFirstAvailableSlot(ReservationCreateViewModel model)
    {
        for (var offset = 0; offset <= ReservationSchedulePolicy.MaximumAdvanceDays; offset++)
        {
            var date = ReservationSchedulePolicy.Today.AddDays(offset);
            var slots = await slotService.GetSlotsAsync(date, model.GuestCount, model.PreferredAreaId);
            if (slots.Slots.Count == 0) continue;
            model.ReservationDate = date;
            model.ReservationTime = TimeOnly.Parse(slots.Slots[0]);
            return;
        }
        model.ReservationDate = ReservationSchedulePolicy.Today;
    }
}
