using System.Collections.Generic;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services
{
    public interface IQuanLyMonStore
    {
        // NhomMon (Category) Management
        IEnumerable<NhomMon> LayTatCaNhomMon();
        IEnumerable<NhomMon> LayNhomMonDangSuDung();
        NhomMon? LayNhomMon(int id);
        NhomMon AddNhomMon(NhomMon nhom);
        bool SuaTenNhomMon(int id, string? ten);
        bool DatTrangThaiNhomMon(int id, bool dangSuDung);
        bool XoaNhomMon(int id);
        void LuuThuTuNhomMon(IReadOnlyList<int> ids, IReadOnlyList<int> banDau);

        // MonAn (Dish/Menu Item) Management
        IEnumerable<MonAn> LayTatCaMonAn();
        IEnumerable<MonAn> LayMonAnTheoNhom(int nhomMonId);
        MonAn ThemMonAn(MonAn mon);
        MonAn? LayMonAn(int id);
        MonAn? CapNhatMonAn(MonAn updated);

        // Public Menu (Thực đơn công khai)
        IReadOnlyList<NhomMonThucDon> LayThucDonTheoNhom();

        // DonHang (Order) Management
        DonHang TaoDonHang();
        DonHang? LayDonHang(int donHangId);
        DongHang ThemDongHang(int donHangId, DongHang dong);
        IEnumerable<DongHang> LayDongHangTheoDon(int donHangId);
        IEnumerable<DonHang> LayTatCaDonHang();

        // Audit (Nhật ký thay đổi giá)
        IEnumerable<RestaurantManagement.Data.Models.GhiNhanThayDoiGia> LayNhatKyGiaChoMon(int monAnId);
    }
}
