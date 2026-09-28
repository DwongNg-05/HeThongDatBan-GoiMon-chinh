using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.QuanLyNhomMon;

public class IndexModel(IQuanLyMonStore store) : PageModel
{
    public IReadOnlyList<NhomMon> NhomMon { get; private set; } = [];
    [TempData] public string? ThongBao { get; set; }
    public void OnGet() => NhomMon = store.LayTatCaNhomMon().ToArray();
}
