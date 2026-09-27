using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using System.Data;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = "Manager,Waiter")]
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
    public async Task<IActionResult> Index()
    {
        var reservations = new List<ReservationListItemViewModel>();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(ReservationQuery + " ORDER BY r.StartsAt DESC, r.Id DESC;", connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) reservations.Add(ReadReservation(reader));
        return View(reservations);
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
        StartsAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("StartsAt")), DateTimeKind.Utc), GetVietnamTimeZone()),
        EndsAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("EndsAt")), DateTimeKind.Utc), GetVietnamTimeZone()),
        Status = reader.GetString(reader.GetOrdinal("Status"))
    };
    // HIỂN THỊ FORM ĐẶT BÀN
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var defaultTime = BookingTime.NextStart(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetVietnamTimeZone()));

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


        // =====================================================
        // KIỂM TRA MỐC 30 PHÚT
        // =====================================================

        if (model.StartsAt.Ticks % TimeSpan.FromMinutes(30).Ticks != 0)
        {
            ModelState.AddModelError(
                nameof(model.StartsAt),
                "Thời gian đặt bàn phải theo mốc 30 phút."
            );

            return View(model);
        }


        // =====================================================
        // KIỂM TRA KHÔNG ĐƯỢC ĐẶT TRONG QUÁ KHỨ
        // =====================================================

        if (model.StartsAt <= TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetVietnamTimeZone()))
        {
            ModelState.AddModelError(
                nameof(model.StartsAt),
                "Thời gian đặt bàn phải ở tương lai."
            );

            return View(model);
        }


        // =====================================================
        // KIỂM TRA GIỜ HOẠT ĐỘNG
        // =====================================================

        var selectedTime =
            model.StartsAt.TimeOfDay;

        var openingTime =
            new TimeSpan(8, 0, 0);

        var latestStartTime =
            new TimeSpan(21, 30, 0);

        if (selectedTime < openingTime ||
            selectedTime > latestStartTime)
        {
            ModelState.AddModelError(
                nameof(model.StartsAt),
                "Thời gian đặt bàn phải từ 08:00 đến 21:30."
            );

            return View(model);
        }


        // =====================================================
        // KIỂM TRA KHU VỰC
        // =====================================================

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
                GetVietnamTimeZone()
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
            ModelState.AddModelError(ex.Number switch { 51407 or 51408 => nameof(model.PreferredAreaId), 51409 => nameof(model.GuestCount), 51003 or 51004 => nameof(model.StartsAt), _ => string.Empty }, ex.Message);
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

    private static TimeZoneInfo GetVietnamTimeZone()
    {
        try
        {
            // Windows
            return TimeZoneInfo.FindSystemTimeZoneById(
                "SE Asia Standard Time"
            );
        }
        catch (TimeZoneNotFoundException)
        {
            // Linux
            return TimeZoneInfo.FindSystemTimeZoneById(
                "Asia/Ho_Chi_Minh"
            );
        }
    }
}
