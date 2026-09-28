using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;
using System.Text.Json;

namespace RestaurantManagement.Web.Pages.GoiMon
{
    public class CheckoutModel : PageModel
    {
        private readonly IQuanLyMonStore _store;

        public CheckoutModel(IQuanLyMonStore store)
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
            var mons = _store.LayTatCaMonAn().ToDictionary(m => m.Id);
            foreach (var it in items)
            {
                if (!mons.TryGetValue(it.monId, out var mon) || mon.TrangThai != TrangThaiMon.DangBan)
                {
                    return BadRequest("Giỏ hàng chứa món không tồn tại hoặc đã ngưng bán");
                }
            }

            var don = _store.TaoDonHang();
            foreach (var it in items)
            {
                var mon = mons[it.monId];
                var dong = new DongHang
                {
                    TenMonTaiThoiDiem = mon.Ten,
                    GiaBanTaiThoiDiemVnd = mon.GiaBanVnd,
                    DonViTinh = mon.DonViTinh,
                    SoLuong = it.soLuong
                };
                _store.ThemDongHang(don.Id, dong);
            }

            return RedirectToPage("/DonHang/Details", new { id = don.Id });
        }

        private class CartItem { public int monId { get; set; } public int soLuong { get; set; } public int gia { get; set; } public string? ten { get; set; } }
    }
}
