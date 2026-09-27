using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.QuanLyNhomMon;

[Authorize(Roles = "Manager")]
public class TaoModel(InMemoryQuanLyMonStore store) : PageModel
{
    [BindProperty] public string? Ten { get; set; }
    [BindProperty] public bool DangSuDung { get; set; } = true;
    [BindProperty, Required(ErrorMessage = "Vui lòng nhập thứ tự hiển thị.")]
    [Range(0, int.MaxValue, ErrorMessage = "Thứ tự hiển thị phải là số nguyên không âm.")]
    public int? ThuTuHienThi { get; set; }
    [TempData] public string? ThongBao { get; set; }

    public void OnGet()
    {
        var max = store.LayTatCaNhomMon().Select(n => n.ThuTuHienThi).DefaultIfEmpty(0).Max();
        ThuTuHienThi = max == int.MaxValue ? max : max + 1;
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        try
        {
            store.AddNhomMon(new NhomMon { Ten = Ten ?? "", DangSuDung = DangSuDung, ThuTuHienThi = ThuTuHienThi!.Value });
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(nameof(Ten), ex.Message);
            return Page();
        }
        ThongBao = "Thêm nhóm món thành công.";
        return RedirectToPage("Index");
    }
}
