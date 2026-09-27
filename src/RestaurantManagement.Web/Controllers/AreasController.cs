using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Areas;
using System.Data;

namespace RestaurantManagement.Web.Controllers;

[Authorize(Roles = "Manager")]
public class AreasController(IConfiguration configuration) : Controller
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new AreaListViewModel();
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("dbo.usp_ListAreas", connection) { CommandType = CommandType.StoredProcedure };
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) model.Areas.Add(ReadArea(reader));
        return View(model);
    }

    [HttpGet]
    public IActionResult Create() => View(new AreaFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Create(AreaFormViewModel model) => Save(model, false);

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var area = await GetArea(id);
        if (area is null) return NotFound();
        return View(new AreaFormViewModel { Id = area.Id, Name = area.Name, SortOrder = area.SortOrder, Notes = area.Notes });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(AreaFormViewModel model) => Save(model, true);

    private async Task<IActionResult> Save(AreaFormViewModel model, bool editing)
    {
        if (!ModelState.IsValid) return View(editing ? "Edit" : "Create", model);
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand(editing ? "dbo.usp_UpdateArea" : "dbo.usp_CreateArea", connection)
                { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = GetActorUserId();
            if (editing) command.Parameters.Add("@AreaId", SqlDbType.Int).Value = model.Id;
            command.Parameters.Add("@Name", SqlDbType.NVarChar, -1).Value = model.Name;
            command.Parameters.Add("@SortOrder", SqlDbType.Int).Value = model.SortOrder!.Value;
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, -1).Value = (object?)model.Notes ?? DBNull.Value;
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
            TempData["Success"] = editing ? "Cập nhật khu vực thành công." : "Thêm khu vực thành công.";
            return RedirectToAction(nameof(Index));
        }
        catch (SqlException ex) when (ex.Number == 51404) { return NotFound(); }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản hiện tại không có quyền quản lý khu vực. Vui lòng kiểm tra tài khoản và cấu hình ActorUserId.");
            return View(editing ? "Edit" : "Create", model);
        }
        catch (SqlException ex) when (ex.Number is 51401 or 51402 or 51403 or 51405 or 2601 or 2627)
        {
            var field = ex.Number switch { 51403 => nameof(model.SortOrder), 51405 => nameof(model.Notes), _ => nameof(model.Name) };
            ModelState.AddModelError(field, ex.Number is 51402 or 2601 or 2627 ? "Tên khu vực đã tồn tại." : ex.Message);
            return View(editing ? "Edit" : "Create", model);
        }
    }

    // GET only displays confirmation; cancelling never sends a mutation request.
    [HttpGet, ActionName("Deactivate")]
    public async Task<IActionResult> ConfirmDeactivate(int id)
    {
        var area = await GetArea(id);
        return area is null ? NotFound() : View("ConfirmDeactivate", area);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(int id) => ChangeState(id, false);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Delete(int id) => ChangeState(id, true);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Reactivate(int id) => ChangeState(id, false, true);

    private async Task<IActionResult> ChangeState(int id, bool deleting, bool reactivating = false)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString);
            await using var command = new SqlCommand(reactivating ? "dbo.usp_ReactivateArea" : deleting ? "dbo.usp_DeleteArea" : "dbo.usp_DeactivateArea", connection)
                { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@ActorUserId", SqlDbType.Int).Value = GetActorUserId();
            command.Parameters.Add("@AreaId", SqlDbType.Int).Value = id;
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
            TempData["Success"] = reactivating ? "Khu vực đã chuyển sang Đang sử dụng." : deleting ? "Xóa khu vực thành công." : "Khu vực đã chuyển sang Ngừng sử dụng.";
        }
        catch (SqlException ex) when (ex.Number == 51404) { return NotFound(); }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            TempData["Error"] = "Tài khoản hiện tại không có quyền quản lý khu vực. Vui lòng kiểm tra tài khoản và cấu hình ActorUserId.";
        }
        catch (SqlException ex) when (ex.Number is 51007 or 51008 or 547)
        {
            TempData["Error"] = ex.Number == 547
                ? "Khu vực đã có dữ liệu liên quan nên không thể xóa. Vui lòng chuyển sang ngừng sử dụng."
                : ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<AreaViewModel?> GetArea(int id)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("dbo.usp_GetArea", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@AreaId", SqlDbType.Int).Value = id;
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadArea(reader) : null;
    }

    private static AreaViewModel ReadArea(SqlDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
        Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
    };

    // Demo actor until the application's authentication story is implemented.
    private int GetActorUserId() => int.TryParse(configuration["AreaManagement:ActorUserId"], out var id) && id > 0 ? id : 0;
}

