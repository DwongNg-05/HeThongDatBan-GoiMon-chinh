using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using System.Data;

namespace RestaurantManagement.Web.Controllers;

public class ReservationsController : Controller
{
    private readonly IConfiguration _configuration;

    public ReservationsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // =========================================================
    // CONNECTION STRING
    // =========================================================

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Chưa cấu hình ConnectionStrings:DefaultConnection.");


    // =========================================================
    // DANH SÁCH ĐẶT BÀN
    // =========================================================

    [HttpGet]
    public IActionResult Index() => View(DailyReservationRules.Today(DateTime.UtcNow));

    [HttpGet]
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
    [HttpGet]
    public async Task<IActionResult> Details(long id)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(ReservationQuery + " WHERE r.Id=@id;", connection);
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? View(ReadReservation(reader)) : NotFound();
    }

    private const string ReservationQuery = """
        SELECT r.Id,r.Code,r.CustomerName,r.Phone,r.GuestCount,
            COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName,r.StartsAt,r.EndsAt,r.Status
        FROM dbo.Reservations r LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
        """;

    private static ReservationListItemViewModel ReadReservation(SqlDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        Code = reader.GetString(reader.GetOrdinal("Code")),
        CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
        Phone = reader.GetString(reader.GetOrdinal("Phone")),
        GuestCount = reader.GetInt32(reader.GetOrdinal("GuestCount")),
        AreaName = reader.IsDBNull(reader.GetOrdinal("AreaName")) ? null : reader.GetString(reader.GetOrdinal("AreaName")),
        StartsAt = VietnamTime.FromUtc(reader.GetDateTime(reader.GetOrdinal("StartsAt"))),
        EndsAt = VietnamTime.FromUtc(reader.GetDateTime(reader.GetOrdinal("EndsAt"))),
        Status = reader.GetString(reader.GetOrdinal("Status"))
    };
    // HIỂN THỊ FORM ĐẶT BÀN
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var defaultTime = BookingTime.NextStart(VietnamTime.Now);

        var model = new ReservationCreateViewModel
        {
            CustomerName = string.Empty,

            Phone = string.Empty,

            GuestCount = 2,

            StartsAt = defaultTime,

            // Không chọn khu vực mặc định
            PreferredAreaId = null,

            Email = null,

            Notes = null
        };

        // Chỉ lấy khu vực đang hoạt động
        await LoadActiveAreas(model);

        return View(model);
    }


    // =========================================================
    // XỬ LÝ ĐẶT BÀN
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        ReservationCreateViewModel model)
    {
        // Luôn load lại danh sách khu vực
        await LoadActiveAreas(model);

        // =====================================================
        // KIỂM TRA MODEL
        // =====================================================

        if (!ModelState.IsValid)
        {
            return View(model);
        }


        if (model.PreferredAreaId.HasValue)
        {
            bool areaIsActive =
                model.Areas.Any(
                    x => x.Id == model.PreferredAreaId.Value
                );

            if (!areaIsActive)
            {
                ModelState.AddModelError(
                    nameof(model.PreferredAreaId),
                    "Khu vực bạn chọn không còn hoạt động."
                );

                return View(model);
            }
        }


        // =====================================================
        // CHUYỂN GIỜ VIỆT NAM -> UTC
        // =====================================================

        DateTime vietnamTime =
            DateTime.SpecifyKind(
                model.StartsAt,
                DateTimeKind.Unspecified
            );

        DateTime utcStartsAt =
            TimeZoneInfo.ConvertTimeToUtc(
                vietnamTime,
                VietnamTime.Zone
            );


        try
        {
            // =================================================
            // KẾT NỐI DATABASE
            // =================================================

            await using var connection =
                new SqlConnection(ConnectionString);


            // =================================================
            // GỌI STORED PROCEDURE
            // =================================================

            await using var command =
                new SqlCommand(
                    "dbo.usp_CreateReservation",
                    connection
                )
                {
                    CommandType =
                        CommandType.StoredProcedure
                };


            // =================================================
            // TÊN KHÁCH
            // =================================================

            command.Parameters.Add(
                new SqlParameter(
                    "@CustomerName",
                    SqlDbType.NVarChar,
                    100
                )
                {
                    Value =
                        model.CustomerName.Trim()
                }
            );


            // =================================================
            // SỐ ĐIỆN THOẠI
            // =================================================

            command.Parameters.Add(
                new SqlParameter(
                    "@Phone",
                    SqlDbType.VarChar,
                    10
                )
                {
                    Value =
                        model.Phone.Trim()
                }
            );


            // =================================================
            // SỐ KHÁCH
            // =================================================

            command.Parameters.Add(
                new SqlParameter(
                    "@GuestCount",
                    SqlDbType.Int
                )
                {
                    Value =
                        model.GuestCount
                }
            );


            // =================================================
            // THỜI GIAN UTC
            // =================================================

            command.Parameters.Add(
                new SqlParameter(
                    "@StartsAt",
                    SqlDbType.DateTime2
                )
                {
                    Value =
                        utcStartsAt
                }
            );


            // =================================================
            // KHU VỰC
            // =================================================

            command.Parameters.Add(
                new SqlParameter(
                    "@PreferredAreaId",
                    SqlDbType.Int
                )
                {
                    Value =
                        (object?)model.PreferredAreaId
                        ?? DBNull.Value
                }
            );


            // =================================================
            // EMAIL
            // =================================================

            command.Parameters.Add(
                new SqlParameter(
                    "@Email",
                    SqlDbType.NVarChar,
                    254
                )
                {
                    Value =
                        string.IsNullOrWhiteSpace(model.Email)
                            ? DBNull.Value
                            : model.Email.Trim()
                }
            );


            // =================================================
            // GHI CHÚ
            // =================================================

            command.Parameters.Add(
                new SqlParameter(
                    "@Notes",
                    SqlDbType.NVarChar,
                    500
                )
                {
                    Value =
                        string.IsNullOrWhiteSpace(model.Notes)
                            ? DBNull.Value
                            : model.Notes.Trim()
                }
            );


            // =================================================
            // MỞ KẾT NỐI
            // =================================================

            await connection.OpenAsync();


            // =================================================
            // THỰC THI
            // =================================================

            await command.ExecuteNonQueryAsync();


            // =================================================
            // THÀNH CÔNG
            // =================================================

            TempData["Success"] =
                "Đặt bàn thành công.";

            return RedirectToAction(
                nameof(Success)
            );
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            // Refresh a stale selection when an area was deactivated during submission.
            await LoadActiveAreas(model);
            ModelState.AddModelError(ex.Number switch { 51407 or 51408 => nameof(model.PreferredAreaId), 51409 => nameof(model.GuestCount), 51003 or 51004 or 51410 or 51411 or 51412 => nameof(model.StartsAt), _ => string.Empty }, ex.Message);
            return View(model);
        }
    }
    // TRANG ĐẶT BÀN THÀNH CÔNG
    // =========================================================

    [HttpGet]
    public IActionResult Success()
    {
        return View();
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CheckSchedule([ModelBinder(BinderType = typeof(VietnamBookingTimeBinder))] DateTime? startsAt)
    {
        if (!ModelState.IsValid || startsAt is null)
            return Json(new { allowed = false, message = "Vui lòng chọn ngày và giờ hợp lệ." });
        try
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startsAt.Value, DateTimeKind.Unspecified), VietnamTime.Zone);
            await using var cn = new SqlConnection(ConnectionString);
            await using var cmd = new SqlCommand("dbo.usp_ValidateBookingSchedule", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@StartsAt", SqlDbType.DateTime2).Value = utc;
            await cn.OpenAsync(); await cmd.ExecuteNonQueryAsync();
            return Json(new { allowed = true, message = "Thời gian hợp lệ. Bạn có thể tiếp tục đặt bàn." });
        }
        catch (SqlException ex) when (ex.Number is 51003 or 51004 or 51410 or 51411 or 51412)
        {
            return Json(new { allowed = false, message = ex.Message });
        }
    }


    // =========================================================
    // LOAD KHU VỰC ĐANG HOẠT ĐỘNG
    // =========================================================

    private async Task LoadActiveAreas(
        ReservationCreateViewModel model)
    {
        model.Areas.Clear();

        const string sql = """
            SELECT
                Id,
                Name,
                SortOrder
            FROM dbo.Areas
            WHERE IsActive = 1
            ORDER BY SortOrder, Name, Id;
            """;

        await using var connection =
            new SqlConnection(ConnectionString);

        await using var command =
            new SqlCommand(
                sql,
                connection
            );

        await connection.OpenAsync();

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.Areas.Add(
                new BookingAreaOption
                {
                    Id =
                        reader.GetInt32(
                            reader.GetOrdinal("Id")
                        ),

                    Name =
                        reader.GetString(
                            reader.GetOrdinal("Name")
                        ),

                    SortOrder =
                        reader.GetInt32(
                            reader.GetOrdinal("SortOrder")
                        )
                }
            );
        }
    }


    // =========================================================
    // TIMEZONE VIỆT NAM
    // =========================================================

}
