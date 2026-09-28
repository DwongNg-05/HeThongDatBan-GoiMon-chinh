using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;
using System.Linq;

namespace RestaurantManagement.Web.Pages.DonHang
{
    public class DetailsModel : PageModel
    {
        private readonly IQuanLyMonStore _store;

        public DetailsModel(IQuanLyMonStore store)
        {
            _store = store;
        }

        public global::RestaurantManagement.Data.Models.DonHang? DonHang { get; set; }
        public IEnumerable<global::RestaurantManagement.Data.Models.DongHang> DongHang { get; set; } = Enumerable.Empty<global::RestaurantManagement.Data.Models.DongHang>();

        public IActionResult OnGet(int id)
        {
            var don = _store.LayDonHang(id);
            if (don == null) return NotFound();
            DonHang = don;
            DongHang = _store.LayDongHangTheoDon(id);
            return Page();
        }
    }
}
