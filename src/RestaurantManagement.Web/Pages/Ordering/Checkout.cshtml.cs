using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;
using System.Text.Json;

namespace RestaurantManagement.Web.Pages.Ordering
{
    // S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public class CheckoutModel : PageModel
    {
        private readonly IMenuStore _store;
        private readonly DailyDishStore? _daily;

        // daily = null: dùng trong kiểm thử với store bộ nhớ (không có SQL Server).
        public CheckoutModel(IMenuStore store, DailyDishStore? daily = null)
        {
            _store = store;
            _daily = daily;
        }

        [BindProperty]
        public string CartJson { get; set; } = string.Empty;

        public IActionResult OnPost()
        {
            if (string.IsNullOrEmpty(CartJson)) return BadRequest("Giỏ hàng rỗng");

            var items = JsonSerializer.Deserialize<List<CartItem>>(CartJson) ?? new List<CartItem>();
            if (items.Count == 0) return BadRequest("Giỏ hàng rỗng");

            // Validate all items exist and are currently being sold
            var dishesById = _store.GetAllDishes().ToDictionary(m => m.Id);
            foreach (var it in items)
            {
                if (!dishesById.TryGetValue(it.dishId, out var dish) || dish.Status != DishStatus.OnSale || _store.GetCategory(dish.CategoryId)?.IsActive != true)
                {
                    return BadRequest("Giỏ hàng chứa món không tồn tại, đã ngưng bán hoặc thuộc nhóm ngừng sử dụng");
                }
            }

            // S2-08 Task 1: món đang tạm hết (hoặc hết trong ngày) không nhận order mới, kể cả khi đã nằm sẵn trong giỏ.
            var availability = _daily?.AvailabilityNow();
            var unavailableItems = items
                .Where(it => availability?.IsUnavailable(it.dishId) == true)
                .Select(it => (it.dishId, dishesById[it.dishId].Name))
                .Distinct()
                .ToArray();
            var unavailable = unavailableItems.Select(item => item.Name).ToArray();
            if (unavailable.Length > 0)
            {
                var message = $"Món đang tạm hết: {string.Join(", ", unavailable)}. Đã cập nhật giỏ hàng; vui lòng kiểm tra lại trước khi gửi đơn.";
                if (Request.Headers.XRequestedWith == "XMLHttpRequest")
                {
                    return StatusCode(StatusCodes.Status409Conflict, new
                    {
                        message,
                        unavailableDishIds = unavailableItems.Select(item => item.dishId).ToArray(),
                        unavailableNames = unavailable
                    });
                }

                return BadRequest(message);
            }

            var order = _store.CreateOrder();
            foreach (var it in items)
            {
                var dish = dishesById[it.dishId];
                var line = new OrderLine
                {
                    DishNameSnapshot = dish.Name,
                    UnitPriceVnd = dish.PriceVnd,
                    Unit = dish.Unit,
                    Quantity = it.quantity
                };
                _store.AddOrderLine(order.Id, line);
            }

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                return new JsonResult(new { redirectUrl = Url.Page("/Orders/Details", new { id = order.Id }) });
            }

            return RedirectToPage("/Orders/Details", new { id = order.Id });
        }

        private class CartItem { public int dishId { get; set; } public int quantity { get; set; } public int price { get; set; } public string? name { get; set; } }
    }
}
