using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.QuanLyNhomMon;

[Authorize(Roles = "Manager")]
public class TrangThaiModel(InMemoryQuanLyMonStore store) : PageModel
{
    public NhomMon Nhom { get; private set; } = new();
    public int SoMon { get; private set; }
    [TempData] public string? ThongBao { get; set; }

    public IActionResult OnGet(int id)
    {
        var nhom = store.LayNhomMon(id);
        if (nhom is null) return NotFound();
        Nhom = nhom;
        SoMon = store.LayMonAnTheoNhom(id).Count();
        return Page();
    }

    public IActionResult OnPostNgung(int id) => Luu(id, false);
    public IActionResult OnPostBatLai(int id) => Luu(id, true);

    private IActionResult Luu(int id, bool active)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!store.DatTrangThaiNhomMon(id, active)) return NotFound();
        ThongBao = active ? "Đã bật lại nhóm món." : "Đã ngừng sử dụng nhóm món. Dữ liệu nhóm và món ăn được giữ nguyên.";
        return RedirectToPage("Index");
    }
}
