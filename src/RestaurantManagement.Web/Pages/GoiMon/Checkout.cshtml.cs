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
        private readonly IConfiguration? _configuration;

        public CheckoutModel(IQuanLyMonStore store,IConfiguration? configuration = null)
        {
            _store = store;
            _configuration=configuration;
        }

        [BindProperty]
        public string CartJson { get; set; } = string.Empty;
        [BindProperty] public long SessionId { get; set; }
        [BindProperty] public Guid RequestId { get; set; }

        public IActionResult OnPost()
        {
            if (string.IsNullOrEmpty(CartJson)) return BadRequest("Giỏ hàng rỗng");

            List<CartItem> items;
            try { items = JsonSerializer.Deserialize<List<CartItem>>(CartJson) ?? new List<CartItem>(); }
            catch (JsonException) { return BadRequest("Dữ liệu giỏ món không hợp lệ."); }
            if (items.Count == 0) return BadRequest("Giỏ hàng rỗng");

            // Validate all items exist and are currently being sold
            var mons = _store.LayTatCaMonAn().ToDictionary(m => m.Id);
            foreach (var it in items)
            {
                if (!mons.TryGetValue(it.monId, out var mon) || mon.TrangThai != TrangThaiMon.DangBan || _store.LayNhomMon(mon.NhomMonId)?.DangSuDung != true)
                {
                    return BadRequest("Giỏ hàng chứa món không tồn tại, đã ngưng bán hoặc thuộc nhóm ngừng sử dụng");
                }
            }

            if (_configuration is not null)
            {
                if (SessionId<=0 || RequestId==Guid.Empty || items.Any(i=>i.soLuong is <1 or >99))
                    return BadRequest("Vui lòng chọn bàn đang phục vụ và số lượng hợp lệ.");
                var actor=int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
                var json=JsonSerializer.Serialize(items.Select(i=>new { MenuItemId=i.monId,Quantity=i.soLuong }));
                try
                {
                    new OrderWorkflowStore(_configuration.GetConnectionString("DefaultConnection")!).Submit(SessionId,RequestId,json,actor).GetAwaiter().GetResult();
                    return RedirectToAction("Index","Orders");
                }
                catch (Microsoft.Data.SqlClient.SqlException ex) when(ex.Number is >=51000 and <51600)
                { return BadRequest(ex.Message); }
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
