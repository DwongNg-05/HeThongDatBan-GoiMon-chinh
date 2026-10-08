using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.GoiMon;

public class IndexModel(IQuanLyMonStore store,IConfiguration? configuration = null) : PageModel
{
    public IReadOnlyList<NhomMonThucDon> NhomMon { get; private set; } = [];
    [NonHandler] public void OnGet() => NhomMon=store.LayThucDonTheoNhom();

    public async Task OnGetAsync()
    {
        OnGet();
        if (configuration is null) return;
        var actor=int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        ViewData["OrderSessions"]=await new OrderWorkflowStore(configuration.GetConnectionString("DefaultConnection")!).ActiveSessions(actor);
    }
}
