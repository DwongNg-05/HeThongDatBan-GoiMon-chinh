using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;
using System.Linq;

namespace RestaurantManagement.Web.Pages.Orders
{
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
