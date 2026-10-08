using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RestaurantManagement.Web.Pages.GuestOrdering;

[AllowAnonymous]
public sealed class SuccessModel : PageModel
{
    public long Order { get; private set; }
    public bool IsValid { get; private set; }

    public void OnGet(long order)
    {
        Order = order;
        IsValid = order > 0;
    }
}
