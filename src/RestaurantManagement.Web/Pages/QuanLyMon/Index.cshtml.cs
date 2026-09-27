using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

public class IndexModel : PageModel
{
    private readonly InMemoryQuanLyMonStore _store;

    public IndexModel(InMemoryQuanLyMonStore store)
    {
        _store = store;
    }

    public IEnumerable<MonAn> DanhSachMonAn { get; set; } = Enumerable.Empty<MonAn>();
    public Dictionary<int, string> LookupNhom { get; set; } = new();

    public void OnGet()
    {
        DanhSachMonAn = _store.LayTatCaMonAn();
        LookupNhom = _store.LayTatCaNhomMon().ToDictionary(n => n.Id, n => n.Ten);
    }
}
