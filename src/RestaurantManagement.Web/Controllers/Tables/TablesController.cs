using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Tables;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>Quản lý danh mục bàn và mã QR công khai của từng bàn.</summary>
/// <remarks>
/// S1-07 is deliberately independent from login. Until S1-04 is merged,
/// QR operations use the configured development actor permitted by the database.
/// </remarks>
public class TablesController(
    IConfiguration configuration,
    TableQrService qrService,
    TableQrPdfBuilder pdfBuilder) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    // Danh sách bàn nay nằm trong màn hình "Khu vực & bàn"; giữ đường dẫn cũ để không gãy liên kết.
    [HttpGet]
    public IActionResult Index() => RedirectToAction("Index", "Areas");

    [HttpGet]
    public async Task<IActionResult> Create(int? areaId)
        => View(await PopulateAreas(new DiningTableFormViewModel { AreaId = areaId ?? 0 }));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DiningTableFormViewModel model)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            ModelState.AddModelError(string.Empty, "Chưa cấu hình tài khoản thực hiện thao tác QR.");
            return View(await PopulateAreas(model));
        }

        Normalize(model);
        if (await IsCodeUsed(model.Code, null))
            ModelState.AddModelError(nameof(model.Code), "Mã bàn đã tồn tại trong nhà hàng.");
        if (!ModelState.IsValid) return View(await PopulateAreas(model));

        try
        {
            var id = await InsertTableAsync(model);
            await qrService.RotateAsync(id, actorUserId);
            TempData["Success"] = $"Đã thêm bàn {model.Code} và sinh mã QR.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            ModelState.AddModelError(nameof(model.Code), "Mã bàn đã tồn tại trong nhà hàng.");
            return View(await PopulateAreas(model));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        const string sql = "SELECT Id, Code, AreaId, MinCapacity, MaxCapacity, TableType, Status FROM dbo.DiningTables WHERE Id = @Id;";
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return NotFound();

        var model = new DiningTableFormViewModel
        {
            Id = reader.GetInt32(0), Code = reader.GetString(1), AreaId = reader.GetInt32(2),
            MinCapacity = reader.GetInt32(3), MaxCapacity = reader.GetInt32(4),
            TableType = reader.GetString(5), Status = reader.GetString(6)
        };
        return View(await PopulateAreas(model));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DiningTableFormViewModel model)
    {
        Normalize(model);
        if (await IsCodeUsed(model.Code, model.Id))
            ModelState.AddModelError(nameof(model.Code), "Mã bàn đã tồn tại trong nhà hàng.");
        if (!ModelState.IsValid) return View(await PopulateAreas(model));

        const string sql = """
            UPDATE dbo.DiningTables
            SET AreaId = @AreaId, Code = @Code, MinCapacity = @MinCapacity, MaxCapacity = @MaxCapacity,
                TableType = @TableType, Status = @Status, StatusChangedAt = SYSUTCDATETIME()
            WHERE Id = @Id;
            """;
        try
        {
            if (await ExecuteSave(sql, model, true) == 0) return NotFound();
            TempData["Success"] = $"Đã cập nhật bàn {model.Code}.";
            return BackToArea(model.AreaId);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            ModelState.AddModelError(nameof(model.Code), "Mã bàn đã tồn tại trong nhà hàng.");
            return View(await PopulateAreas(model));
        }
    }

    /// <summary>
    /// Quản lý xoá bàn. Bàn chưa từng dùng thì xoá hẳn; bàn đã có lịch sử (đặt bàn, phục vụ, món đã gọi)
    /// được chuyển sang "Ngừng sử dụng" để giữ lịch sử. Bàn đang phục vụ hoặc còn lượt đặt sắp tới thì không xoá được.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId)) return Forbid();
        int? areaId = null;
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using (var area = new SqlCommand("SELECT AreaId FROM dbo.DiningTables WHERE Id=@id;", connection))
            {
                area.Parameters.Add("@id", SqlDbType.Int).Value = id;
                areaId = await area.ExecuteScalarAsync() as int?;
            }
            if (areaId is null) return NotFound();
            await using var command = new SqlCommand("dbo.usp_DeleteTable", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@TableId", SqlDbType.Int).Value = id;
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = actorUserId;
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            var result = reader.GetString(0);
            var code = reader.GetString(1);
            TempData["Success"] = result == "Deleted"
                ? $"Đã xoá bàn {code}."
                : $"Bàn {code} đã có lịch sử đặt bàn/phục vụ nên được chuyển sang \"Ngừng sử dụng\" thay vì xoá hẳn (giữ lịch sử, không nhận đặt bàn mới, mã QR đã bị thu hồi).";
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and < 51500)
        {
            TempData["Error"] = ex.Message;
        }
        return BackToArea(areaId ?? 0);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var details = await qrService.GetDetailsAsync(id);
        return details is null ? NotFound() : View(ToViewModel(details));
    }

    // Inline image used by the manager detail page.
    [HttpGet]
    public async Task<IActionResult> QrImage(int id)
    {
        var details = await qrService.GetDetailsAsync(id);
        if (details?.PublicToken is null) return NotFound();
        return File(TableQrService.CreatePng(BuildPublicQrUrl(details.PublicToken)), "image/png");
    }

    [HttpGet]
    public async Task<IActionResult> DownloadQrImage(int id)
    {
        var details = await qrService.GetDetailsAsync(id);
        if (details?.PublicToken is null) return NotFound();
        return File(TableQrService.CreatePng(BuildPublicQrUrl(details.PublicToken)), "image/png", $"qr-{details.TableCode}.png");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> GenerateQr(int id) => RotateQrCore(id, false);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> RotateQr(int id) => RotateQrCore(id, true);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadQrPdf(int areaId)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            TempData["Error"] = "Chưa cấu hình tài khoản thực hiện thao tác QR.";
            return BackToArea(areaId);
        }

        try
        {
            var items = await qrService.GetAreaPrintItemsAsync(areaId, actorUserId);
            if (items.Count == 0)
            {
                TempData["Error"] = "Khu vực được chọn chưa có bàn đang hoạt động để xuất mã QR.";
                return BackToArea(areaId);
            }
            if (items.Any(item => item.PublicToken is null))
                throw new InvalidOperationException("Không thể sinh mã QR cho tất cả bàn trong khu vực.");

            var documentItems = items.Select(item => new TableQrPdfDocumentItem(
                item.TableCode, BuildPublicQrUrl(item.PublicToken!)));
            var pdf = pdfBuilder.Build(documentItems);
            return File(pdf, "application/pdf", $"ma-qr-khu-vuc-{areaId}.pdf");
        }
        catch (SqlException ex) when (ex.Number == 51021)
        {
            TempData["Error"] = "Có bàn không còn hoạt động nên chưa thể sinh mã QR.";
            return BackToArea(areaId);
        }
    }

    // Used by the form to provide immediate feedback before it is submitted.
    [HttpGet]
    public async Task<IActionResult> CheckCode(string? code, int? id)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized)) return Json(new { available = true });
        return Json(new { available = !await IsCodeUsed(normalized, id) });
    }

    private async Task<IActionResult> RotateQrCore(int id, bool replacingExisting)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            TempData["Error"] = "Chưa cấu hình tài khoản thực hiện thao tác QR.";
            return RedirectToAction(nameof(Details), new { id });
        }
        try
        {
            await qrService.RotateAsync(id, actorUserId);
            TempData["Success"] = replacingExisting
                ? "Đã sinh mã QR mới. Mã QR cũ đã hết hiệu lực ngay lập tức."
                : "Đã sinh mã QR cho bàn này.";
        }
        catch (SqlException ex) when (ex.Number == 51021)
        {
            TempData["Error"] = "Bàn không tồn tại hoặc đã ngừng hoạt động.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> IsCodeUsed(string code, int? excludingId)
    {
        const string sql = "SELECT COUNT(*) FROM dbo.DiningTables WHERE Code = @Code AND (@Id IS NULL OR Id <> @Id);";
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Code", SqlDbType.VarChar, 20).Value = code;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)excludingId ?? DBNull.Value;
        await connection.OpenAsync();
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private async Task<int> InsertTableAsync(DiningTableFormViewModel model)
    {
        const string sql = """
            INSERT dbo.DiningTables (AreaId, Code, MinCapacity, MaxCapacity, TableType, Status, SortOrder)
            OUTPUT INSERTED.Id
            VALUES (@AreaId, @Code, @MinCapacity, @MaxCapacity, @TableType, @Status,
                (SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM dbo.DiningTables WHERE AreaId = @AreaId));
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@AreaId", SqlDbType.Int).Value = model.AreaId;
        command.Parameters.Add("@Code", SqlDbType.VarChar, 20).Value = model.Code;
        command.Parameters.Add("@MinCapacity", SqlDbType.Int).Value = model.MinCapacity;
        command.Parameters.Add("@MaxCapacity", SqlDbType.Int).Value = model.MaxCapacity;
        command.Parameters.Add("@TableType", SqlDbType.VarChar, 20).Value = model.TableType;
        command.Parameters.Add("@Status", SqlDbType.VarChar, 20).Value = model.Status;
        await connection.OpenAsync();
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> ExecuteSave(string sql, DiningTableFormViewModel model, bool includeId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        if (includeId) command.Parameters.Add("@Id", SqlDbType.Int).Value = model.Id;
        command.Parameters.Add("@AreaId", SqlDbType.Int).Value = model.AreaId;
        command.Parameters.Add("@Code", SqlDbType.VarChar, 20).Value = model.Code;
        command.Parameters.Add("@MinCapacity", SqlDbType.Int).Value = model.MinCapacity;
        command.Parameters.Add("@MaxCapacity", SqlDbType.Int).Value = model.MaxCapacity;
        command.Parameters.Add("@TableType", SqlDbType.VarChar, 20).Value = model.TableType;
        command.Parameters.Add("@Status", SqlDbType.VarChar, 20).Value = model.Status;
        await connection.OpenAsync();
        return await command.ExecuteNonQueryAsync();
    }

    private async Task<DiningTableFormViewModel> PopulateAreas(DiningTableFormViewModel model)
    {
        model.Areas = await GetAreaOptionsAsync();
        return model;
    }

    private async Task<List<SelectListItem>> GetAreaOptionsAsync()
    {
        const string sql = "SELECT Id, Name FROM dbo.Areas WHERE IsActive = 1 ORDER BY SortOrder, Name;";
        var areas = new List<SelectListItem>();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sql, connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            areas.Add(new SelectListItem(reader.GetString(1), reader.GetInt32(0).ToString()));
        return areas;
    }

    private string BuildPublicQrUrl(string token)
    {
        var configuredBaseUrl = configuration["TableQr:PublicBaseUrl"]?.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}{Request.PathBase}"
            : configuredBaseUrl.TrimEnd('/');
        return $"{baseUrl}/q/{Uri.EscapeDataString(token)}";
    }

    private TableQrDetailsViewModel ToViewModel(TableQrDetails details) => new()
    {
        TableId = details.TableId,
        TableCode = details.TableCode,
        AreaName = details.AreaName,
        MinCapacity = details.MinCapacity,
        MaxCapacity = details.MaxCapacity,
        TableType = details.TableType,
        Status = details.Status,
        IsActive = details.IsActive,
        HasQr = details.PublicToken is not null,
        QrUrl = details.PublicToken is null ? null : BuildPublicQrUrl(details.PublicToken),
        CreatedAt = details.CreatedAt
    };

    private bool TryGetActorUserId(out int id)
        => int.TryParse(configuration["AreaManagement:ActorUserId"], out id) && id > 0;

    private IActionResult BackToArea(int areaId) =>
        Redirect(Url.Action("Index", "Areas") + "#khu-vuc-" + areaId);

    private static void Normalize(DiningTableFormViewModel model) => model.Code = model.Code.Trim().ToUpperInvariant();
}
