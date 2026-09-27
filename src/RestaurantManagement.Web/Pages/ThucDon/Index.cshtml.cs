using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.ThucDon;

public class IndexModel(InMemoryQuanLyMonStore store) : PageModel
{
    public IReadOnlyList<NhomMonThucDon> NhomMon { get; private set; } = [];

    public void OnGet() => NhomMon = store.LayThucDonTheoNhom();
}
