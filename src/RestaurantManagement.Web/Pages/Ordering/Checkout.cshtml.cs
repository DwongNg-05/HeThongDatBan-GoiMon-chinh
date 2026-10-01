using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;
using System.Text.Json;

namespace RestaurantManagement.Web.Pages.Ordering
{
    public class CheckoutModel : PageModel
    {
        private readonly IMenuStore _store;

        public CheckoutModel(IMenuStore store)
        {
            _store = store;
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

            return RedirectToPage("/Orders/Details", new { id = order.Id });
        }

        private class CartItem { public int dishId { get; set; } public int quantity { get; set; } public int price { get; set; } public string? name { get; set; } }
    }
}
