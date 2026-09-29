using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

public class TaoModel : PageModel
{
    private readonly IQuanLyMonStore _store;

    public TaoModel(IQuanLyMonStore store)
    {
        _store = store;
    }

    [BindProperty]
    public MonAn Mon { get; set; } = new MonAn();

    public IEnumerable<NhomMon> DanhSachNhom { get; set; } = Enumerable.Empty<NhomMon>();

    public void OnGet()
    {
        DanhSachNhom = _store.LayTatCaNhomMon();
    }

        public IActionResult OnPost()
        {
            DanhSachNhom = _store.LayTatCaNhomMon();

            if (!DanhSachNhom.Any(n => n.Id == Mon.NhomMonId))
                ModelState.AddModelError("Mon.NhomMonId", "Nhóm món không tồn tại.");

            if (!ModelState.IsValid)
            {
                return Page();
            }

            try { _store.ThemMonAn(Mon); }
            catch (ArgumentException)
            {
                ModelState.AddModelError("Mon.NhomMonId", "Nhóm món không còn tồn tại. Vui lòng chọn nhóm khác.");
                DanhSachNhom = _store.LayTatCaNhomMon();
                return Page();
            }
            return RedirectToPage("/QuanLyMon/Index");
        }
}
