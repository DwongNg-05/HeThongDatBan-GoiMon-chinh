using System.Collections.Generic;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services
{
    public interface IQuanLyMonStore
    {
        IEnumerable<NhomMon> LayTatCaNhomMon();
        NhomMon AddNhomMon(NhomMon nhom);

        IEnumerable<MonAn> LayTatCaMonAn();
        MonAn ThemMonAn(MonAn mon);
        MonAn? LayMonAn(int id);
        MonAn? CapNhatMonAn(MonAn updated);

        DonHang TaoDonHang();
        DonHang? LayDonHang(int donHangId);
        DongHang ThemDongHang(int donHangId, DongHang dong);
        IEnumerable<DongHang> LayDongHangTheoDon(int donHangId);
        IEnumerable<DonHang> LayTatCaDonHang();

        IEnumerable<RestaurantManagement.Data.Models.GhiNhanThayDoiGia> LayNhatKyGiaChoMon(int monAnId);
    }
}
