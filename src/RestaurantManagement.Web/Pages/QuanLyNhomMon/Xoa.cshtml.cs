using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.QuanLyNhomMon;

public class XoaModel(IQuanLyMonStore store) : PageModel
{
    public NhomMon Nhom { get; private set; } = new();
    public IReadOnlyList<MonAn> MonAn { get; private set; } = [];
    [TempData] public string? ThongBao { get; set; }

    public IActionResult OnGet(int id) => Tai(id);

    public IActionResult OnPost(int id)
    {
        if (!ModelState.IsValid) return Tai(id);
        try
        {
            if (!store.XoaNhomMon(id)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return Tai(id);
        }
        ThongBao = "Xóa nhóm món thành công.";
        return RedirectToPage("Index");
    }

    private IActionResult Tai(int id)
    {
        var nhom = store.LayNhomMon(id);
        if (nhom is null) return NotFound();
        Nhom = nhom;
        MonAn = store.LayMonAnTheoNhom(id).ToArray();
        return Page();
    }
}
