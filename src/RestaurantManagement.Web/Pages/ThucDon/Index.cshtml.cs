using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.ThucDon;

[Authorize(Roles = "Manager")]
public class IndexModel(InMemoryQuanLyMonStore store) : PageModel
{
    public IReadOnlyList<NhomMonThucDon> NhomMon { get; private set; } = [];

    public void OnGet() => NhomMon = store.LayThucDonTheoNhom();
}
