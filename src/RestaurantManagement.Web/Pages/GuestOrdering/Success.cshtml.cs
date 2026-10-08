using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using RestaurantManagement.Web.Models.Ordering;

namespace RestaurantManagement.Web.Pages.GuestOrdering;

[AllowAnonymous]
public sealed class SuccessModel : PageModel
{
    public GuestOrderReceipt? Receipt { get; private set; }
    public bool IsValid { get; private set; }

    public void OnGet(long order)
    {
        var rawReceipt = HttpContext.Session.GetString("GuestOrdering.Receipt");
        Receipt = string.IsNullOrWhiteSpace(rawReceipt) ? null : JsonSerializer.Deserialize<GuestOrderReceipt>(rawReceipt);
        IsValid = order > 0 && Receipt?.BatchId == order;
    }
}
