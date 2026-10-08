using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;
using System.Linq;

namespace RestaurantManagement.Web.Pages.Orders
{
    // S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
    [Authorize(Roles = AppRoles.FrontOfHouse)]
    public class DetailsModel : PageModel
    {
        private readonly IMenuStore _store;

        public DetailsModel(IMenuStore store)
        {
            _store = store;
        }

        public global::RestaurantManagement.Data.Models.Order? Order { get; set; }
        public IEnumerable<global::RestaurantManagement.Data.Models.OrderLine> Lines { get; set; } = Enumerable.Empty<global::RestaurantManagement.Data.Models.OrderLine>();

        public IActionResult OnGet(int id)
        {
            var order = _store.GetOrder(id);
            if (order == null) return NotFound();
            Order = order;
            Lines = _store.GetOrderLines(id);
            return Page();
        }
    }
}
