using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Pages.Ordering;

[Authorize(Roles = AppRoles.FrontOfHouse)]
public class CheckoutModel : PageModel
{
private readonly IMenuStore _store;
private readonly DailyDishStore? _daily;
private readonly SessionOrderingService? _sessionOrdering;

public CheckoutModel(
IMenuStore store,
DailyDishStore? daily = null,
SessionOrderingService? sessionOrdering = null)
{
_store = store;
_daily = daily;
_sessionOrdering = sessionOrdering;
}

[BindProperty]
public string CartJson { get; set; } = string.Empty;

[BindProperty]
public int TableId { get; set; }

[BindProperty]
public long SessionId { get; set; }

[BindProperty]
public Guid RequestId { get; set; }

// Giữ điểm gọi của kiểm thử hiện có.
[NonHandler]
public IActionResult OnPost()
{
return OnPostAsync().GetAwaiter().GetResult();
}

public async Task<IActionResult>
OnPostAsync()
{
if (string.IsNullOrWhiteSpace(CartJson))
{
return BadRequest("Giỏ món rỗng.");
}

List<CartItem>
? items;

try
{
items = JsonSerializer.Deserialize<List<CartItem>>(CartJson);
}
catch (JsonException)
{
return BadRequest("Dữ liệu giỏ món không hợp lệ.");
}

if (items is null || items.Count == 0)
{
return BadRequest("Giỏ món rỗng.");
}

if (items.Count > 100 || items.Any(item =>
item is null
|| item.dishId <= 0
|| item.quantity < 1
               || item.quantity > 99))
{
return BadRequest(
"Giỏ món tối đa 100 dòng; số lượng mỗi dòng từ 1 đến 99.");
}

var dishesById = _store.GetAllDishes()
.ToDictionary(dish => dish.Id);

foreach (var item in items)
{
if (!dishesById.TryGetValue(item.dishId, out var dish)
|| dish.Status != DishStatus.OnSale
|| _store.GetCategory(dish.CategoryId)?.IsActive != true)
{
return BadRequest(
"Giỏ món chứa món không tồn tại, đã ngừng bán "
+ "hoặc thuộc nhóm ngừng sử dụng.");
}
}

if (TableId <= 0 || SessionId <= 0 || RequestId == Guid.Empty)
{
return BadRequest(
"Thiếu thông tin bàn, phiên hoặc mã yêu cầu. "
+ "Vui lòng chọn bàn từ sơ đồ.");
}

if (_sessionOrdering is null)
{
return StatusCode(503, "Chưa cấu hình dịch vụ gọi món.");
}

if (!int.TryParse(
User.FindFirstValue(ClaimTypes.NameIdentifier),
out var actorUserId) || actorUserId <= 0)
{
return Forbid();
}

var cancellationToken = HttpContext.RequestAborted;

try
{
var currentSession =
await _sessionOrdering.GetByTableAsync(
TableId, cancellationToken);

if (currentSession is null)
{
return BadRequest(
"Bàn không còn phiên đang mở. "
+ "Vui lòng quay lại sơ đồ bàn.");
}

// Không chuyển giỏ món cũ sang phiên mới của cùng bàn.
if (currentSession.SessionId != SessionId)
{
return BadRequest(
"Phiên của bàn đã thay đổi. "
+ "Vui lòng mở lại màn hình gọi món từ sơ đồ bàn.");
}

var availability = _daily?.AvailabilityNow();

var unavailableNames = items
.Where(item =>
availability?.IsUnavailable(item.dishId) == true)
.Select(item => dishesById[item.dishId].Name)
.Distinct()
.ToArray();

if (unavailableNames.Length > 0)
{
return BadRequest(
"Món đang tạm hết: "
+ string.Join(", ", unavailableNames)
+ ". Vui lòng bỏ khỏi giỏ và gọi lại.");
}

var submittedItems = items.Select(item =>
new SessionOrderingService.SubmitOrderItem
{
MenuItemId = item.dishId,
Quantity = item.quantity
}).ToList();

// Database kiểm tra lại trạng thái tại lúc ghi món.
// Cùng RequestId trong cùng phiên không tạo thêm đợt trùng.
await _sessionOrdering.SubmitAsync(
SessionId,
RequestId,
submittedItems,
actorUserId,
cancellationToken);

return RedirectToPage("/Ordering/Index", new
{
tableId = TableId,
submitted = true
});
}
catch (SqlException exception)
when (exception.Number >= 51000
&& exception.Number <= 51999)
{
return BadRequest(exception.Message);
}
}

public sealed class CartItem
{
public int dishId { get; set; }

public int quantity { get; set; }
}
}