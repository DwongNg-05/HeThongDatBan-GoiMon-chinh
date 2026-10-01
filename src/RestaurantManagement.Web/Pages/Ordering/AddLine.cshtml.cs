using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.Ordering
{
    public class AddLineModel : PageModel
    {
        private readonly IMenuStore _store;

        public AddLineModel(IMenuStore store)
        {
            _store = store;
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
