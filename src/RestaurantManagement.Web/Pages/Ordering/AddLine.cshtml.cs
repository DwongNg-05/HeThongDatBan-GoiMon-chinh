using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.Ordering
{
    // S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public class AddLineModel : PageModel
    {
        private readonly IMenuStore _store;
        private readonly DailyDishStore? _daily;

        public AddLineModel(IMenuStore store, DailyDishStore? daily = null)
        {
            _store = store;
            _daily = daily;
        }

        [BindProperty]
        public int DishId { get; set; }

        [BindProperty]
        public int Quantity { get; set; } = 1;

        // POST: add a OrderLine to an open Order (or create one)
        public IActionResult OnPost(int dishId)
        {
            // Find the Dish and ensure it is currently being sold
            var dish = _store.GetDish(dishId);
            if (dish == null || dish.Status != DishStatus.OnSale || _store.GetCategory(dish.CategoryId)?.IsActive != true)
            {
                return BadRequest("Món không tồn tại hoặc không đang bán");
            }
            // S2-08 Task 1: món đang tạm hết (hoặc hết trong ngày) không nhận order mới.
            if (_daily?.AvailabilityNow().IsUnavailable(dishId) == true)
            {
                return BadRequest($"Món {dish.Name} đang tạm hết, không nhận order mới.");
            }

            // For demo: use a single open Order or create new
            var order = _store.GetAllOrders().FirstOrDefault(d => d.IsOpen) ?? _store.CreateOrder();

            var line = new OrderLine
            {
                DishNameSnapshot = dish.Name,
                UnitPriceVnd = dish.PriceVnd,
                Unit = dish.Unit,
                Quantity = 1,
            };

            _store.AddOrderLine(order.Id, line);

            return RedirectToPage("/Orders/Details", new { id = order.Id });
        }
    }
}
