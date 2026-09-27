using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

public class TaoModel : PageModel
{
    private readonly InMemoryQuanLyMonStore _store;

    public TaoModel(InMemoryQuanLyMonStore store)
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

            if (!ModelState.IsValid)
            {
                return Page();
            }

            _store.ThemMonAn(Mon);
            return RedirectToPage("/QuanLyMon/Index");
        }
}
