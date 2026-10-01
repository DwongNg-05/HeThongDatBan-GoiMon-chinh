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

        // Thực đơn cho khách (không cần đăng nhập): chỉ nhóm đang sử dụng và món đang bán,
        // kèm ảnh, mô tả ngắn, giá VND và cờ hết trong ngày. Xem docs/S2-01-Task1.md.
        IReadOnlyList<NhomThucDonCongKhai> LayThucDonCongKhai();

        // DonHang (Order) Management
        DonHang TaoDonHang();
        DonHang? LayDonHang(int donHangId);
        DongHang ThemDongHang(int donHangId, DongHang dong);
        IEnumerable<DongHang> LayDongHangTheoDon(int donHangId);
        IEnumerable<DonHang> LayTatCaDonHang();

        // Audit (Nhật ký thay đổi giá)
        IEnumerable<RestaurantManagement.Data.Models.GhiNhanThayDoiGia> LayNhatKyGiaChoMon(int monAnId);
        // Các lần đổi giá gần nhất của mọi món (mới nhất trước), hiển thị trong màn hình Quản lý món.
        IEnumerable<RestaurantManagement.Data.Models.GhiNhanThayDoiGia> LayNhatKyGiaGanDay(int soDong);
    }
}
