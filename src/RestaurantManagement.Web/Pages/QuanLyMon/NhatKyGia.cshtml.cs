using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

public class NhatKyGiaModel : PageModel
{
    private readonly InMemoryQuanLyMonStore _store;

    public NhatKyGiaModel(InMemoryQuanLyMonStore store)
    {
        _store = store;
    }

    public IEnumerable<GhiNhanThayDoiGia> NhatKy { get; set; } = Enumerable.Empty<GhiNhanThayDoiGia>();
    public MonAn? Mon { get; set; }

    public IActionResult OnGet(int id)
    {
        Mon = _store.LayMonAn(id);
        if (Mon == null) return NotFound();
        NhatKy = _store.LayNhatKyGiaChoMon(id);
        return Page();
    }
}
