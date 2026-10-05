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
    public async Task<IActionResult> Index()
    {
        var reservations = new List<ReservationListItemViewModel>();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(Query + " ORDER BY r.StartsAt DESC, r.Id DESC;", connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) reservations.Add(Read(reader));
        return View(reservations);
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
