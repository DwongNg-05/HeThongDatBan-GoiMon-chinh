using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using System.Data;

namespace RestaurantManagement.Web.Controllers;

[Authorize]
[Route("ReservationManagement")]
public class ReservationManagementController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    [HttpGet("")]
    public IActionResult Index() => View(DailyReservationRules.Today(DateTime.UtcNow));

    [HttpGet("Daily")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Daily(string? date, CancellationToken cancellationToken, string? status = null)
    {
        var now = DateTime.UtcNow;
        var selected = DailyReservationRules.Today(now);
        if (date is not null && (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out selected) || selected.Year is < 1900 or > 9998))
            return BadRequest(new { message = "Ngày xem không hợp lệ." });
        if (!DailyReservationRules.IsSupportedFilter(status))
            return BadRequest(new { message = "Trạng thái lọc không hợp lệ." });

        try
        {
            return Json(await new DailyReservationStore(ConnectionString).Read(selected, cancellationToken, status, now));
        }
        catch (SqlException)
        {
            return StatusCode(503, new { message = "Không thể tải danh sách đặt bàn. Vui lòng thử lại." });
        }
    }

    [HttpGet("Details/{id:long}")]
    public async Task<IActionResult> Details(long id)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(Query + " WHERE r.Id=@id;", connection);
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? View(Read(reader)) : NotFound();
    }

    private const string Query = """
        SELECT r.Id, r.Code, r.CustomerName, r.Phone, r.GuestCount,
          COALESCE(r.AreaNameSnapshot, a.Name) AS AreaName, r.StartsAt, r.EndsAt, r.Status, r.Notes
        FROM dbo.Reservations r LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
        """;

    private static ReservationListItemViewModel Read(SqlDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        Code = reader.GetString(reader.GetOrdinal("Code")),
        CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
        Phone = reader.GetString(reader.GetOrdinal("Phone")),
        GuestCount = reader.GetInt32(reader.GetOrdinal("GuestCount")),
        AreaName = reader.IsDBNull(reader.GetOrdinal("AreaName")) ? null : reader.GetString(reader.GetOrdinal("AreaName")),
        StartsAt = VietnamTime.FromUtc(reader.GetDateTime(reader.GetOrdinal("StartsAt"))),
        EndsAt = VietnamTime.FromUtc(reader.GetDateTime(reader.GetOrdinal("EndsAt"))),
        Status = reader.GetString(reader.GetOrdinal("Status")),
        Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes"))
    };
}
