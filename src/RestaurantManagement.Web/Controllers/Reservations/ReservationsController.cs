using RestaurantManagement.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;
using System.Data;
using System.Security.Claims;

namespace RestaurantManagement.Web.Controllers;

public class ReservationsController : Controller
{
    /// <summary>
    /// Các vai trò được xem danh sách và chi tiết khách đặt trước (mã bàn, giờ, số khách, ghi chú…).
    /// S1-04 Task 2: chỉ Quản lý, Phục vụ. Bếp và Thu ngân bị chặn ở máy chủ (403).
    /// </summary>
    public const string ReservationReaders = AppRoles.FrontOfHouse;

    /// <summary>
    /// S1-04 Task 4 (cũ): tạo đặt bàn chỉ dành cho Quản lý, Phục vụ.
    /// Nay form đặt bàn công khai: khách tự đặt bàn không cần đăng nhập ([AllowAnonymous] ở Create, Success,
    /// BookingEmailStatus, CheckSchedule, AvailableTables). Nhân viên vẫn đặt hộ khách như trước.
    /// </summary>
    public const string ReservationWriters = AppRoles.FrontOfHouse;

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
    [Authorize(Roles = ReservationReaders)]
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
    [Authorize(Roles = ReservationReaders)]
    public async Task<IActionResult> Details(long id, [FromServices] IReservationEmailStatusStore emails, CancellationToken cancellationToken)
    {
        var reservation = await FindReservation(id, cancellationToken);
        if (reservation is null) return NotFound();
        var panel = await LoadEmailPanel(reservation, emails, cancellationToken);
        return View(new ReservationDetailsViewModel(reservation, panel));
    }

    // =========================================================
    // QUẢN LÝ HUỶ ĐẶT BÀN
    // =========================================================

    /// <summary>
    /// Quản lý huỷ một lượt đặt bàn đang chờ xác nhận hoặc đã xác nhận (bắt buộc ghi lý do).
    /// Bàn được trả lại cho khách khác; khách có email nhận ngay email báo huỷ.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Manager)]
    public async Task<IActionResult> Cancel(long id, string? reason, [FromServices] BookingEmailDispatcher bookingEmails)
    {
        var reservation = await FindReservation(id, HttpContext.RequestAborted);
        if (reservation is null) return NotFound();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId)) return Forbid();
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "Vui lòng nhập lý do huỷ đặt bàn.";
            return RedirectToAction(nameof(Details), new { id });
        }
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_StaffCancelReservation", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@ReservationId", SqlDbType.BigInt).Value = id;
            command.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = reason.Trim().Length > 500 ? reason.Trim()[..500] : reason.Trim();
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        var email = await bookingEmails.SendBookingCancelledAsync(id);
        var code = reservation.TableCode ?? reservation.Code;
        TempData["Success"] = email.Outcome switch
        {
            BookingEmailOutcome.Sent => $"Đã huỷ đặt bàn {code}. Đã gửi email báo huỷ cho khách.",
            BookingEmailOutcome.Failed => $"Đã huỷ đặt bàn {code}. Email báo huỷ chưa gửi được, vui lòng gọi điện báo khách ({reservation.Phone}).",
            _ => $"Đã huỷ đặt bàn {code}. Khách không có email, vui lòng gọi điện báo khách ({reservation.Phone})."
        };
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Quản lý xoá hẳn một lượt đặt bàn đã huỷ / bị từ chối / khách không đến khỏi danh sách.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = AppRoles.Manager)]
    public async Task<IActionResult> Delete(long id)
    {
        var reservation = await FindReservation(id, HttpContext.RequestAborted);
        if (reservation is null) return NotFound();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId)) return Forbid();
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_DeleteReservation", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@ReservationId", SqlDbType.BigInt).Value = id;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = $"Đã xoá lượt đặt bàn {reservation.TableCode ?? reservation.Code} của {reservation.CustomerName} ({reservation.StartsAt:dd/MM/yyyy HH:mm}).";
        return RedirectToAction(nameof(Index));
    }

    // =========================================================
    // S2-09 Task 3: KHU VỰC EMAIL XÁC NHẬN (tự cập nhật trên màn hình chi tiết)
    // =========================================================

    [HttpGet]
    [Authorize(Roles = ReservationReaders)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> EmailStatus(long id, [FromServices] IReservationEmailStatusStore emails, CancellationToken cancellationToken)
    {
        var reservation = await FindReservation(id, cancellationToken);
        if (reservation is null) return NotFound();
        return PartialView("_EmailStatus", await LoadEmailPanel(reservation, emails, cancellationToken));
    }

    private static async Task<ReservationEmailPanelViewModel> LoadEmailPanel(
        ReservationListItemViewModel reservation, IReservationEmailStatusStore emails, CancellationToken cancellationToken) =>
        ReservationEmailStatus.BuildPanel(reservation.Id, reservation.Code, reservation.Email,
            await emails.GetByReservationAsync(reservation.Id, cancellationToken));

    private async Task<ReservationListItemViewModel?> FindReservation(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(ReservationQuery + " WHERE r.Id=@id;", connection);
        command.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReservation(reader) : null;
    }

    private const string ReservationQuery = """
        SELECT r.Id,r.Code,r.CustomerName,r.Phone,r.Email,r.GuestCount,
            COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName,r.StartsAt,r.EndsAt,r.Status,
            t.Code AS TableCode,ta.Name AS TableAreaName,r.Notes,r.CancelReason,
            r.ConfirmationEmailStatus,r.ConfirmationEmailAttempts
        FROM dbo.Reservations r LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
        LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId LEFT JOIN dbo.Areas ta ON ta.Id=t.AreaId
        """;

    private static ReservationListItemViewModel ReadReservation(SqlDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        Code = reader.GetString(reader.GetOrdinal("Code")),
        CustomerName = reader.GetString(reader.GetOrdinal("CustomerName")),
        Phone = reader.GetString(reader.GetOrdinal("Phone")),
        Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
        GuestCount = reader.GetInt32(reader.GetOrdinal("GuestCount")),
        AreaName = reader.IsDBNull(reader.GetOrdinal("AreaName")) ? null : reader.GetString(reader.GetOrdinal("AreaName")),
        StartsAt = VietnamTime.FromUtc(reader.GetDateTime(reader.GetOrdinal("StartsAt"))),
        EndsAt = VietnamTime.FromUtc(reader.GetDateTime(reader.GetOrdinal("EndsAt"))),
        Status = reader.GetString(reader.GetOrdinal("Status")),
        TableCode = reader.IsDBNull(reader.GetOrdinal("TableCode")) ? null : reader.GetString(reader.GetOrdinal("TableCode")),
        TableAreaName = reader.IsDBNull(reader.GetOrdinal("TableAreaName")) ? null : reader.GetString(reader.GetOrdinal("TableAreaName")),
        Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
        CancelReason = reader.IsDBNull(reader.GetOrdinal("CancelReason")) ? null : reader.GetString(reader.GetOrdinal("CancelReason")),
        // S2-09 Task 2: trạng thái email xác nhận được cập nhật trên lượt đặt bàn sau mỗi lần thử gửi.
        ConfirmationEmailStatus = reader.IsDBNull(reader.GetOrdinal("ConfirmationEmailStatus")) ? null : reader.GetString(reader.GetOrdinal("ConfirmationEmailStatus")),
        ConfirmationEmailAttempts = reader.GetInt32(reader.GetOrdinal("ConfirmationEmailAttempts"))
    };

    // =========================================================
    // KHÁCH XÁC NHẬN ĐẶT BÀN QUA EMAIL (không cần đăng nhập)
    // =========================================================

    /// <summary>
    /// Trang mở từ nút "Xác nhận đặt bàn" trong email. Chỉ hiển thị thông tin và nút xác nhận;
    /// việc xác nhận diễn ra khi khách bấm nút (POST), để trình quét liên kết của hộp thư không tự xác nhận hộ.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Confirm(string? token, [FromServices] BookingConfirmationLinks links, CancellationToken cancellationToken)
    {
        var code = links.ReadToken(token);
        var reservation = code is null ? null : await FindReservationByCode(code, cancellationToken);
        return View(new ReservationConfirmViewModel(token ?? string.Empty, reservation));
    }

    [HttpPost]
    [ActionName("Confirm")]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmPost(string? token, [FromServices] BookingConfirmationLinks links)
    {
        var code = links.ReadToken(token);
        if (code is null) return View("Confirm", new ReservationConfirmViewModel(token ?? string.Empty, null));
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand("dbo.usp_CustomerConfirmReservation", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@Code", SqlDbType.Char, 6).Value = code;
            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            var result = await reader.ReadAsync() ? reader.GetString(reader.GetOrdinal("Result")) : "Confirmed";
            TempData[ReservationConfirmViewModel.SuccessKey] = result == "AlreadyConfirmed"
                ? "Lượt đặt bàn này đã được xác nhận trước đó. Hẹn gặp bạn tại nhà hàng!"
                : "Xác nhận đặt bàn thành công. Nhà hàng sẽ giữ bàn cho bạn. Hẹn gặp bạn tại nhà hàng!";
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData[ReservationConfirmViewModel.ErrorKey] = ex.Message;
        }
        return RedirectToAction(nameof(Confirm), new { token });
    }

    private async Task<ReservationListItemViewModel?> FindReservationByCode(string code, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(ReservationQuery + " WHERE r.Code=@code;", connection);
        command.Parameters.Add("@code", SqlDbType.Char, 6).Value = code;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReservation(reader) : null;
    }

    // =========================================================
    // HIỂN THỊ FORM ĐẶT BÀN
    // =========================================================

    [HttpGet]
    [AllowAnonymous] // Khách đặt bàn không cần đăng nhập (form đặt bàn, trang thành công và API phục vụ form).
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

        if (TempData[RestaurantManagement.Web.Authentication.BookingAntiforgeryRecoveryFilter.FormKey] is string savedForm)
        {
            var fields = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(savedForm)!;
            model.CustomerName = fields.GetValueOrDefault("CustomerName", "");
            model.Phone = fields.GetValueOrDefault("Phone", "");
            model.Email = fields.GetValueOrDefault("Email");
            model.Notes = fields.GetValueOrDefault("Notes");
            if (int.TryParse(fields.GetValueOrDefault("GuestCount"), out var guests)) model.GuestCount = guests;
            if (VietnamTime.TryParseBooking(fields.GetValueOrDefault("StartsAt"), out var start)) model.StartsAt = start;
            if (int.TryParse(fields.GetValueOrDefault("PreferredAreaId"), out var area)) model.PreferredAreaId = area;
            if (DateOnly.TryParseExact(fields.GetValueOrDefault("ReservationDate"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var savedDate) &&
                TimeOnly.TryParse(fields.GetValueOrDefault("ReservationTime"), System.Globalization.CultureInfo.InvariantCulture, out var savedTime))
                model.StartsAt = savedDate.ToDateTime(savedTime);
        }
        model.ReservationDate = DateOnly.FromDateTime(model.StartsAt);
        model.ReservationTime = TimeOnly.FromDateTime(model.StartsAt);

        // Chỉ lấy khu vực đang hoạt động
        await LoadActiveAreas(model);

        return View(model);
    }


    // =========================================================
    // XỬ LÝ ĐẶT BÀN
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous] // Khách đặt bàn không cần đăng nhập (form đặt bàn, trang thành công và API phục vụ form).
    public async Task<IActionResult> Create(
        ReservationCreateViewModel model,
        [FromServices] BookingEmailDispatcher bookingEmails)
    {
        // The public form submits separate Vietnam-local date and time fields.
        if (model.ReservationDate.HasValue && model.ReservationTime.HasValue)
        {
            model.StartsAt = model.ReservationDate.Value.ToDateTime(model.ReservationTime.Value);
            ModelState.Remove(nameof(model.StartsAt));
        }
        model.TableId = null;

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


        long reservationId;
        string reservationCode;
        string? tableCode;
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
            // BÀN KHÁCH CHỌN (mã bàn = mã khách nhận)
            // =================================================

            command.Parameters.Add(
                new SqlParameter("@TableId", SqlDbType.Int)
                {
                    Value = (object?)model.TableId ?? DBNull.Value
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

            // usp_CreateReservation trả về ReservationId và Code của lượt vừa tạo.
            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("usp_CreateReservation không trả về mã đặt bàn.");
                reservationId = Convert.ToInt64(reader["ReservationId"]);
                reservationCode = Convert.ToString(reader["Code"])!.Trim();
                tableCode = reader["TableCode"] is string t && t.Length > 0 ? t : null;
            }
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            // Refresh a stale selection when an area was deactivated during submission.
            await LoadActiveAreas(model);
            ModelState.AddModelError(ex.Number switch { 51413 or 51414 => nameof(model.TableId), 51407 or 51408 => nameof(model.PreferredAreaId), 51409 => nameof(model.GuestCount), 51003 or 51004 or 51410 or 51411 or 51412 => nameof(model.StartsAt), _ => string.Empty }, ex.Message);
            return View(model);
        }

        // =====================================================
        // S2-09 Task 1: lượt đặt bàn ĐÃ được lưu. Gửi email xác nhận ngay;
        // gửi lỗi không ảnh hưởng tới lượt đặt bàn và mã đặt bàn.
        // =====================================================
        var email = await bookingEmails.SendBookingReceivedAsync(reservationId);

        TempData["Success"] = "Đặt bàn thành công.";
        TempData[BookingSuccessViewModel.CodeKey] = reservationCode;
        TempData[BookingSuccessViewModel.ReservationIdKey] = reservationId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TempData[BookingSuccessViewModel.TableCodeKey] = tableCode;
        TempData[BookingSuccessViewModel.StartsAtKey] = vietnamTime.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
        TempData[BookingSuccessViewModel.GuestCountKey] = model.GuestCount;
        TempData[BookingSuccessViewModel.EmailOutcomeKey] = email.Outcome.ToString();
        TempData[BookingSuccessViewModel.EmailToKey] = BookingSuccessViewModel.MaskEmail(email.Recipient ?? model.Email);

        return RedirectToAction(nameof(Success));
    }
    // TRANG ĐẶT BÀN THÀNH CÔNG
    // =========================================================

    /// <summary>
    /// Trang xác nhận đặt bàn: luôn hiển thị đầy đủ mã đặt bàn và kết quả gửi email.
    /// Dùng TempData.Peek để tải lại trang (F5) vẫn thấy mã đặt bàn.
    /// </summary>
    [HttpGet]
    [AllowAnonymous] // Khách đặt bàn không cần đăng nhập (form đặt bàn, trang thành công và API phục vụ form).
    public async Task<IActionResult> Success([FromServices] IReservationEmailStatusStore emails, CancellationToken cancellationToken)
    {
        var model = BookingSuccessViewModel.FromTempData(key => TempData.Peek(key));
        // S2-09 Task 2: hiển thị trạng thái email mới nhất (đang gửi lại / kết quả cuối cùng), không chỉ kết quả lần gửi đầu.
        if (model is { ReservationId: long id, EmailOutcome: not BookingEmailOutcome.NotRequested })
            model = model with { EmailStatus = await LoadCustomerEmailStatus(id, model, emails, cancellationToken) };
        return View(model);
    }

    /// <summary>
    /// S2-09 Task 2: trạng thái email xác nhận cho trang xác nhận đặt bàn (tự cập nhật khi email đang được gửi lại).
    /// Chỉ trả về lượt đặt bàn vừa tạo trong chính trình duyệt này (Id nằm trong TempData, không nhận Id từ URL).
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [AllowAnonymous] // Khách đặt bàn không cần đăng nhập (form đặt bàn, trang thành công và API phục vụ form).
    public async Task<IActionResult> BookingEmailStatus([FromServices] IReservationEmailStatusStore emails, CancellationToken cancellationToken)
    {
        var model = BookingSuccessViewModel.FromTempData(key => TempData.Peek(key));
        if (model?.ReservationId is not long id) return NotFound();
        var status = await LoadCustomerEmailStatus(id, model, emails, cancellationToken);
        if (status is null) return NotFound();
        return Json(new { state = status.State.ToString(), final = status.IsFinal, message = status.Message, cssClass = status.CssClass });
    }

    private static async Task<BookingEmailCustomerStatus?> LoadCustomerEmailStatus(
        long reservationId, BookingSuccessViewModel model, IReservationEmailStatusStore emails, CancellationToken cancellationToken)
    {
        try
        {
            var record = (await emails.GetByReservationAsync(reservationId, cancellationToken))
                .FirstOrDefault(e => e.MessageType == BookingEmailDispatcher.BookingReceived);
            return BookingEmailCustomerStatus.From(record, model.DisplayCode, model.MaskedEmail);
        }
        catch (SqlException)
        {
            // Không đọc được trạng thái: trang vẫn hiển thị mã đặt bàn và kết quả lần gửi đầu.
            return null;
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [AllowAnonymous] // Khách đặt bàn không cần đăng nhập (form đặt bàn, trang thành công và API phục vụ form).
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
        await LoadActiveAreaList(model);
        model.Tables = await FindAvailableTables(model.StartsAt, model.GuestCount, model.PreferredAreaId);
    }

    /// <summary>
    /// Bàn còn trống cho giờ (giờ Việt Nam), số khách và khu vực: chưa có lượt đặt Chờ xác nhận/Đã xác nhận/Đã đến
    /// trong khung giờ giao nhau. Trả về danh sách rỗng khi thông tin chưa hợp lệ.
    /// </summary>
    private async Task<List<BookingTableOption>> FindAvailableTables(DateTime startsAtVietnam, int guestCount, int? areaId)
    {
        var tables = new List<BookingTableOption>();
        if (startsAtVietnam.Year is < 2000 or > 9998 || guestCount is < 1 or > 20) return tables;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("dbo.usp_AvailableTables", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@StartsAt", SqlDbType.DateTime2).Value = VietnamTime.ToUtc(startsAtVietnam);
        command.Parameters.Add("@GuestCount", SqlDbType.Int).Value = guestCount;
        command.Parameters.Add("@PreferredAreaId", SqlDbType.Int).Value = (object?)areaId ?? DBNull.Value;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(new BookingTableOption
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Code = reader.GetString(reader.GetOrdinal("Code")),
                AreaName = reader.GetString(reader.GetOrdinal("AreaName")),
                MaxCapacity = reader.GetInt32(reader.GetOrdinal("MaxCapacity"))
            });
        return tables;
    }

    /// <summary>Danh sách bàn trống cho form đặt bàn (gọi lại khi khách đổi giờ, số khách hoặc khu vực).</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [AllowAnonymous] // Khách đặt bàn không cần đăng nhập (form đặt bàn, trang thành công và API phục vụ form).
    public async Task<IActionResult> AvailableTables(
        [ModelBinder(BinderType = typeof(VietnamBookingTimeBinder))] DateTime? startsAt, int guestCount = 2, int? preferredAreaId = null)
    {
        if (!ModelState.IsValid || startsAt is null)
            return Json(new { tables = Array.Empty<object>(), message = "Vui lòng chọn ngày và giờ hợp lệ." });
        var tables = await FindAvailableTables(startsAt.Value, guestCount, preferredAreaId);
        return Json(new
        {
            tables = tables.Select(t => new { id = t.Id, code = t.Code, area = t.AreaName, capacity = t.MaxCapacity, label = t.Label }),
            message = tables.Count == 0 ? "Không còn bàn trống phù hợp trong khung giờ này. Vui lòng chọn giờ, số khách hoặc khu vực khác." : null
        });
    }

    private async Task LoadActiveAreaList(
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
