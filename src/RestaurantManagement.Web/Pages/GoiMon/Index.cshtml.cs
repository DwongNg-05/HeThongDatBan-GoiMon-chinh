using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;
using System.Linq;

namespace RestaurantManagement.Web.Pages.GoiMon
{
    public class IndexModel : PageModel
    {
        private readonly InMemoryQuanLyMonStore _store;

        public IndexModel(InMemoryQuanLyMonStore store)
        {
            _store = store;
        }

        public IEnumerable<MonAn> MonDangBan { get; set; } = Enumerable.Empty<MonAn>();

        public void OnGet()
        {
            MonDangBan = _store.LayTatCaMonAn().Where(m => m.TrangThai == TrangThaiMon.DangBan);
        }
    }
}
