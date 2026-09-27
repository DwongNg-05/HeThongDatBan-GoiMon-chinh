using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;
using System.Linq;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.ThucDon
{
    public class IndexModel : PageModel
    {
        private readonly InMemoryQuanLyMonStore _store;

        public IndexModel(InMemoryQuanLyMonStore store)
        {
            _store = store;
        }

        // Món đang bán
        public IEnumerable<MonAn> MonDangBan { get; set; } = Enumerable.Empty<MonAn>();

        // Lookup để hiển thị tên nhóm
        public Dictionary<int, string> LookupNhom { get; set; } = new Dictionary<int, string>();

        public void OnGet()
        {
            // Chỉ hiển thị món đang ở trạng thái DangBan
            MonDangBan = _store.LayTatCaMonAn()
                .Where(m => m.TrangThai == TrangThaiMon.DangBan);
            LookupNhom = _store.LayTatCaNhomMon()
                .ToDictionary(n => n.Id, n => n.Ten);
        }
    }
}
