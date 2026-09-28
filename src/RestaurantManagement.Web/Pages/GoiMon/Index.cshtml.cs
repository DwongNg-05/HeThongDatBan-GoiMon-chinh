using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.GoiMon;

public class IndexModel(IQuanLyMonStore store) : PageModel
{
    public IReadOnlyList<NhomMonThucDon> NhomMon { get; private set; } = [];

    public void OnGet() => NhomMon = store.LayThucDonTheoNhom();
}
