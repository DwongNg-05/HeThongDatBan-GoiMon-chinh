using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

public class SuaModel : PageModel
{
    private readonly InMemoryQuanLyMonStore _store;

    public SuaModel(InMemoryQuanLyMonStore store)
    {
        _store = store;
    }

    [BindProperty]
    public MonAn Mon { get; set; } = new MonAn();

    public IEnumerable<NhomMon> DanhSachNhom { get; set; } = Enumerable.Empty<NhomMon>();

    public IActionResult OnGet(int id)
    {
        DanhSachNhom = _store.LayTatCaNhomMon();
        var mon = _store.LayMonAn(id);
        if (mon == null) return NotFound();
        Mon = new MonAn
        {
            Id = mon.Id,
            Ten = mon.Ten,
            NhomMonId = mon.NhomMonId,
            GiaBanVnd = mon.GiaBanVnd,
            DonViTinh = mon.DonViTinh,
            MoTaNgan = mon.MoTaNgan,
            ThoiGianCheBienPhut = mon.ThoiGianCheBienPhut,
            TrangThai = mon.TrangThai
        };
        return Page();
    }

    public IActionResult OnPost()
    {
        DanhSachNhom = _store.LayTatCaNhomMon();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var existing = _store.LayMonAn(Mon.Id);
        if (existing == null) return NotFound();

        // apply validation already enforced by data annotations on Mon
        _store.CapNhatMonAn(Mon);

        return RedirectToPage("/QuanLyMon/Index");
    }
}
