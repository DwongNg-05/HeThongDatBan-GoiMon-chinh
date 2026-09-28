using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.QuanLyNhomMon;

public class SapXepModel(IQuanLyMonStore store) : PageModel
{
    [BindProperty] public List<int> ThuTu { get; set; } = [];
    [BindProperty] public List<int> BanDau { get; set; } = [];
    [TempData] public string? ThongBao { get; set; }
    public IReadOnlyList<NhomMon> NhomMon { get; private set; } = [];

    public void OnGet() => TaiDanhSach();

    public IActionResult OnPost()
    {
        if (ModelState.IsValid)
        {
            try
            {
                store.LuuThuTuNhomMon(ThuTu, BanDau);
                ThongBao = "Lưu thứ tự nhóm món thành công.";
                return RedirectToPage("Index");
            }
            catch (ValidationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        TaiDanhSach();
        return Page();
    }

    private void TaiDanhSach()
    {
        NhomMon = store.LayTatCaNhomMon().ToArray();
        BanDau = NhomMon.Select(n => n.Id).ToList();
    }
}
