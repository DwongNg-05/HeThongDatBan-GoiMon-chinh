using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.GoiMon
{
    [Authorize(Roles = "Manager,Waiter")]
    public class ThemDongModel : PageModel
    {
        private readonly InMemoryQuanLyMonStore _store;

        public ThemDongModel(InMemoryQuanLyMonStore store)
        {
            _store = store;
        }

        [BindProperty]
        public int MonId { get; set; }

        [BindProperty]
        public int SoLuong { get; set; } = 1;

        // POST: add a DongHang to an open DonHang (or create one)
        public IActionResult OnPost(int monId)
        {
            // Find the MonAn and ensure it is currently being sold
            var mon = _store.LayMonAn(monId);
            if (mon == null || mon.TrangThai != TrangThaiMon.DangBan || _store.LayNhomMon(mon.NhomMonId)?.DangSuDung != true)
            {
                return BadRequest("Món không tồn tại hoặc không đang bán");
            }

            // For demo: use a single open DonHang or create new
            var don = _store.LayTatCaDonHang().FirstOrDefault(d => d.IsDangMo) ?? _store.TaoDonHang();

            var dong = new DongHang
            {
                TenMonTaiThoiDiem = mon.Ten,
                GiaBanTaiThoiDiemVnd = mon.GiaBanVnd,
                DonViTinh = mon.DonViTinh,
                SoLuong = 1,
            };

            _store.ThemDongHang(don.Id, dong);

            return RedirectToPage("/DonHang/Details", new { id = don.Id });
        }
    }
}
