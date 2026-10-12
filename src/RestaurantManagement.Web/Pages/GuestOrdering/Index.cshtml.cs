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

    [BindProperty]
    public string RequestId { get; set; } = string.Empty;

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
        var result = await SubmitOrderAsync(cancellationToken);
        if (result.Receipt is not null)
            return RedirectToPage("Success", new { order = result.Receipt.BatchId });

        ErrorMessage = result.ErrorMessage;
        PriceChanges = result.PriceChanges;
        return Page();
    }

    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
    {
        var result = await SubmitOrderAsync(cancellationToken);
        if (result.Receipt is not null)
            return new JsonResult(new { success = true, order = result.Receipt.BatchId });

        if (result.PriceChanges.Count > 0)
            return new JsonResult(new { success = false, message = result.ErrorMessage, priceChanges = result.PriceChanges })
            {
                StatusCode = StatusCodes.Status409Conflict
            };

        return new JsonResult(new { success = false, message = result.ErrorMessage })
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
    }

    private async Task<SubmitResult> SubmitOrderAsync(CancellationToken cancellationToken)
    {
        var context = ReadContext();
        if (context is null)
            return SubmitResult.Failed("Phiên gọi món đã hết hạn. Vui lòng quét lại mã QR tại bàn.");

        if (!Guid.TryParse(RequestId, out var requestId))
            return SubmitResult.Failed("Không xác định được lần gửi order. Vui lòng thử lại.");

        if (!TryReadItems(out var items, out var parseError))
            return SubmitResult.Failed(parseError!);

        try
        {
            var receipt = await orderService.SubmitAsync(context, requestId, items, cancellationToken);
            HttpContext.Session.SetString(ReceiptKey, JsonSerializer.Serialize(receipt));
            return SubmitResult.Succeeded(receipt);
        }
        catch (GuestOrderPriceChangedException exception)
        {
            var priceByDish = exception.Changes.ToDictionary(change => change.DishId, change => change.CurrentPriceVnd);
            CartJson = JsonSerializer.Serialize(items.Select(item => new
            {
                dishId = item.DishId,
                quantity = item.Quantity,
                observedPriceVnd = priceByDish.TryGetValue(item.DishId, out var price) ? price : item.ObservedPriceVnd
            }));
            return SubmitResult.PriceChanged(exception.Message, exception.Changes);
        }
        catch (GuestOrderException exception)
        {
            return SubmitResult.Failed(exception.Message);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            return SubmitResult.Failed("Chưa thể gửi order. Vui lòng kiểm tra mạng và thử lại.");
        }
    }

    private bool TryReadItems(out List<GuestOrderLineInput> items, out string? error)
    {
        try
        {
            items = JsonSerializer.Deserialize<List<CartLineDto>>(CartJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?.Select(line => new GuestOrderLineInput(line.DishId, line.Quantity, line.ObservedPriceVnd)).ToList() ?? [];
            CartJson = JsonSerializer.Serialize(items.Select(item => new
            {
                dishId = item.DishId,
                quantity = item.Quantity,
                observedPriceVnd = item.ObservedPriceVnd
            }));
            error = null;
            return true;
        }
        catch (JsonException)
        {
            items = [];
            CartJson = "[]";
            error = "Giỏ món không hợp lệ. Vui lòng chọn lại món.";
            return false;
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

    private sealed record SubmitResult(GuestOrderReceipt? Receipt, string? ErrorMessage,
        IReadOnlyList<GuestOrderPriceChange> PriceChanges)
    {
        public static SubmitResult Succeeded(GuestOrderReceipt receipt) => new(receipt, null, []);
        public static SubmitResult Failed(string error) => new(null, error, []);
        public static SubmitResult PriceChanged(string error, IReadOnlyList<GuestOrderPriceChange> changes) => new(null, error, changes);
    }
}
