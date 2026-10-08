using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Models.Ordering;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Services.Ordering;

namespace RestaurantManagement.Web.Pages.GuestOrdering;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(IMenuStore menuStore, GuestOrderService orderService) : PageModel
{
    private const string SessionIdKey = "GuestOrdering.SessionId";
    private const string GuestTokenKey = "GuestOrdering.Token";
    private const string ReceiptKey = "GuestOrdering.Receipt";

    [BindProperty]
    public string CartJson { get; set; } = "[]";

    public IReadOnlyList<PublicMenuCategory> Categories { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public IReadOnlyList<GuestOrderPriceChange> PriceChanges { get; private set; } = [];
    public bool HasOrderingSession { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? qr, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(qr))
        {
            try
            {
                var context = await orderService.StartFromQrAsync(qr, cancellationToken);
                HttpContext.Session.SetString(SessionIdKey, context.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                HttpContext.Session.SetString(GuestTokenKey, context.GuestToken);
                return RedirectToPage(); // Do not retain the public QR token in the address bar.
            }
            catch (GuestOrderException exception)
            {
                ErrorMessage = exception.Message;
            }
            catch (Microsoft.Data.SqlClient.SqlException exception)
            {
                ErrorMessage = exception.Message;
            }
        }

        LoadPage();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        LoadPage();
        var context = ReadContext();
        if (context is null)
        {
            ErrorMessage = "Phiên gọi món đã hết hạn. Vui lòng quét lại mã QR tại bàn.";
            return Page();
        }

        List<GuestOrderLineInput>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<CartLineDto>>(CartJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?.Select(line => new GuestOrderLineInput(line.DishId, line.Quantity, line.ObservedPriceVnd)).ToList();
        }
        catch (JsonException)
        {
            CartJson = "[]";
            ErrorMessage = "Giỏ món không hợp lệ. Vui lòng chọn lại món.";
            return Page();
        }

        // Keep only server-recognised numeric fields when a failed post
        // is rendered again. The browser cannot inject arbitrary text back into
        // the JSON block used to restore its cart.
        CartJson = JsonSerializer.Serialize((items ?? []).Select(item => new
        {
            dishId = item.DishId,
            quantity = item.Quantity,
            observedPriceVnd = item.ObservedPriceVnd
        }));

        try
        {
            var receipt = await orderService.SubmitAsync(context, items ?? [], cancellationToken);
            HttpContext.Session.SetString(ReceiptKey, JsonSerializer.Serialize(receipt));
            HttpContext.Session.Remove(SessionIdKey);
            HttpContext.Session.Remove(GuestTokenKey);
            return RedirectToPage("Success", new { order = receipt.BatchId });
        }
        catch (GuestOrderPriceChangedException exception)
        {
            PriceChanges = exception.Changes;
            var priceByDish = exception.Changes.ToDictionary(change => change.DishId, change => change.CurrentPriceVnd);
            CartJson = JsonSerializer.Serialize((items ?? []).Select(item => new
            {
                dishId = item.DishId,
                quantity = item.Quantity,
                observedPriceVnd = priceByDish.TryGetValue(item.DishId, out var price) ? price : item.ObservedPriceVnd
            }));
            ErrorMessage = exception.Message;
            return Page();
        }
        catch (GuestOrderException exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
        catch (Microsoft.Data.SqlClient.SqlException exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
    }

    private void LoadPage()
    {
        Categories = menuStore.GetPublicMenu();
        HasOrderingSession = ReadContext() is not null;
    }

    private GuestOrderContext? ReadContext()
    {
        var rawId = HttpContext.Session.GetString(SessionIdKey);
        var token = HttpContext.Session.GetString(GuestTokenKey);
        return long.TryParse(rawId, out var sessionId) && sessionId > 0 && !string.IsNullOrWhiteSpace(token)
            ? new GuestOrderContext(sessionId, token)
            : null;
    }

    private sealed class CartLineDto
    {
        public int DishId { get; init; }
        public int Quantity { get; init; }
        public int ObservedPriceVnd { get; init; }
    }
}
