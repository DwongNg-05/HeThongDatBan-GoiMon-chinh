using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.QuanLyNhomMon;

public class SuaModel(InMemoryQuanLyMonStore store) : PageModel
{
    [BindProperty] public string? Ten { get; set; }
    [TempData] public string? ThongBao { get; set; }

    public IActionResult OnGet(int id)
    {
        var nhom = store.LayNhomMon(id);
        if (nhom is null) return NotFound();
        Ten = nhom.Ten;
        return Page();
    }

    public IActionResult OnPost(int id)
    {
        if (store.LayNhomMon(id) is null) return NotFound();
        if (!ModelState.IsValid) return Page();
        try
        {
            if (!store.SuaTenNhomMon(id, Ten)) return NotFound();
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(nameof(Ten), ex.Message);
            return Page();
        }
        ThongBao = "Sửa tên nhóm món thành công.";
        return RedirectToPage("Index");
    }
}
