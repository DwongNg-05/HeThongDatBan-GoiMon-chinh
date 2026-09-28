using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Data;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Services
{
    public class SqlQuanLyMonStore : IQuanLyMonStore
    {
        private readonly RestaurantDbContext _db;
        private readonly ICurrentUser _user;

        public SqlQuanLyMonStore(RestaurantDbContext db, ICurrentUser user)
        {
            _db = db;
            _user = user;
        }

        public IEnumerable<NhomMon> LayTatCaNhomMon() =>
            _db.NhomMons.OrderBy(n => n.ThuTuHienThi).ThenBy(n => n.Id).AsNoTracking().ToList();

        public IEnumerable<NhomMon> LayNhomMonDangSuDung() =>
            LayTatCaNhomMon().Where(n => n.DangSuDung);

        public NhomMon? LayNhomMon(int id) =>
            _db.NhomMons.AsNoTracking().FirstOrDefault(n => n.Id == id);

        public NhomMon AddNhomMon(NhomMon nhom)
        {
            var ten = KiemTraTenNhom(nhom.Ten);
            if (nhom.ThuTuHienThi < 0)
                throw new ValidationException("Thứ tự hiển thị phải là số nguyên không âm.");
            nhom.Ten = ten;
            _db.NhomMons.Add(nhom);
            _db.SaveChanges();
            return nhom;
        }

        public bool SuaTenNhomMon(int id, string? ten)
        {
            var nhom = _db.NhomMons.FirstOrDefault(n => n.Id == id);
            if (nhom == null) return false;

            nhom.Ten = KiemTraTenNhom(ten, id);
            _db.SaveChanges();
            return true;
        }

        public bool DatTrangThaiNhomMon(int id, bool dangSuDung)
        {
            var nhom = _db.NhomMons.FirstOrDefault(n => n.Id == id);
            if (nhom == null) return false;

            nhom.DangSuDung = dangSuDung;
            _db.SaveChanges();
            return true;
        }

        public bool XoaNhomMon(int id)
        {
            var nhom = _db.NhomMons.FirstOrDefault(n => n.Id == id);
            if (nhom == null) return false;

            var count = _db.MonAns.Count(m => m.NhomMonId == id);
            if (count > 0)
                throw new ValidationException($"Không thể xóa nhóm đang chứa {count} món ăn. Vui lòng chuyển toàn bộ món sang nhóm khác trước khi xóa.");

            _db.NhomMons.Remove(nhom);
            _db.SaveChanges();

            var remaining = _db.NhomMons.OrderBy(n => n.ThuTuHienThi).ThenBy(n => n.Id).ToList();
            for (var i = 0; i < remaining.Count; i++)
                remaining[i].ThuTuHienThi = i + 1;
            _db.SaveChanges();

            return true;
        }

        public void LuuThuTuNhomMon(IReadOnlyList<int> ids, IReadOnlyList<int> banDau)
        {
            var current = _db.NhomMons.OrderBy(n => n.ThuTuHienThi).ThenBy(n => n.Id).Select(n => n.Id).ToArray();
            if (!current.SequenceEqual(banDau))
                throw new ValidationException("Danh sách hoặc thứ tự nhóm món đã thay đổi. Vui lòng tải lại trang và sắp xếp lại.");
            if (ids.Count != current.Length || ids.Distinct().Count() != ids.Count || !ids.ToHashSet().SetEquals(current))
                throw new ValidationException("Thứ tự không hợp lệ: cần đủ mỗi nhóm món đúng một lần.");

            for (var i = 0; i < ids.Count; i++)
            {
                var nhom = _db.NhomMons.FirstOrDefault(n => n.Id == ids[i]);
                if (nhom != null) nhom.ThuTuHienThi = i + 1;
            }
            _db.SaveChanges();
        }

        private string KiemTraTenNhom(string? ten, int? exceptId = null)
        {
            var normalized = QuyTacTenNhomMon.ChuanHoa(ten);
            if (normalized.Length == 0) throw new ValidationException(QuyTacTenNhomMon.TenRong);
            if (normalized.Length > 50) throw new ValidationException(QuyTacTenNhomMon.TenQuaDai);
            if (_db.NhomMons.AsNoTracking().Any(n => n.Id != exceptId &&
                string.Equals(QuyTacTenNhomMon.ChuanHoa(n.Ten), normalized, StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException(QuyTacTenNhomMon.TenTrung);
            return normalized;
        }

        public IEnumerable<MonAn> LayTatCaMonAn() =>
            _db.MonAns.OrderBy(m => m.Id).AsNoTracking().ToList();

        public IEnumerable<MonAn> LayMonAnTheoNhom(int nhomMonId) =>
            _db.MonAns.Where(m => m.NhomMonId == nhomMonId).OrderBy(m => m.Id).AsNoTracking().ToList();

        public MonAn ThemMonAn(MonAn mon)
        {
            KiemTraNhomMon(mon.NhomMonId);
            _db.MonAns.Add(mon);
            _db.SaveChanges();
            return mon;
        }

        public MonAn? LayMonAn(int id) =>
            _db.MonAns.AsNoTracking().FirstOrDefault(m => m.Id == id);

        public IReadOnlyList<NhomMonThucDon> LayThucDonTheoNhom()
        {
            var monDangBan = _db.MonAns.AsNoTracking()
                .Where(m => m.TrangThai == TrangThaiMon.DangBan)
                .ToList()
                .ToLookup(m => m.NhomMonId);

            return LayNhomMonDangSuDung()
                .Select(n => new NhomMonThucDon(n.Id, n.Ten, monDangBan[n.Id].ToArray()))
                .ToList();
        }

        private void KiemTraNhomMon(int nhomMonId)
        {
            if (!_db.NhomMons.AsNoTracking().Any(n => n.Id == nhomMonId))
                throw new ArgumentException("Nhóm món không tồn tại.", nameof(nhomMonId));
        }

        public MonAn? CapNhatMonAn(MonAn updated)
        {
            var existing = _db.MonAns.FirstOrDefault(m => m.Id == updated.Id);
            if (existing == null) return null;

            if (!_user.IsAuthenticated)
                throw new UnauthorizedAccessException("Chưa đăng nhập.");

            var oldPrice = existing.GiaBanVnd;
            var newPrice = updated.GiaBanVnd;

            existing.Ten = updated.Ten;
            existing.NhomMonId = updated.NhomMonId;
            existing.GiaBanVnd = updated.GiaBanVnd;
            existing.DonViTinh = updated.DonViTinh;
            existing.MoTaNgan = updated.MoTaNgan;
            existing.ThoiGianCheBienPhut = updated.ThoiGianCheBienPhut;
            existing.TrangThai = updated.TrangThai;

            if (oldPrice != newPrice)
            {
                _db.GhiNhanThayDoiGias.Add(new GhiNhanThayDoiGia
                {
                    MonAnId = existing.Id,
                    TenMon = existing.Ten,
                    GiaCuVnd = oldPrice,
                    GiaMoiVnd = newPrice,
                    ThoiDiem = DateTime.UtcNow,
                    NguoiSua = _user.UserName!
                });
            }

            _db.SaveChanges();
            return existing;
        }

        public DonHang TaoDonHang()
        {
            var dh = new DonHang { ThoiGianTao = DateTime.UtcNow, IsDangMo = true };
            _db.DonHangs.Add(dh);
            _db.SaveChanges();
            return dh;
        }

        public DonHang? LayDonHang(int donHangId) =>
            _db.DonHangs.AsNoTracking().FirstOrDefault(d => d.Id == donHangId);

        public DongHang ThemDongHang(int donHangId, DongHang dong)
        {
            dong.DonHangId = donHangId;
            _db.DongHangs.Add(dong);
            _db.SaveChanges();
            return dong;
        }

        public IEnumerable<DongHang> LayDongHangTheoDon(int donHangId) =>
            _db.DongHangs.Where(d => d.DonHangId == donHangId).OrderBy(d => d.Id).AsNoTracking().ToList();

        public IEnumerable<DonHang> LayTatCaDonHang() =>
            _db.DonHangs.OrderBy(d => d.Id).AsNoTracking().ToList();

        public IEnumerable<GhiNhanThayDoiGia> LayNhatKyGiaChoMon(int monAnId) =>
            _db.GhiNhanThayDoiGias.Where(e => e.MonAnId == monAnId).OrderByDescending(e => e.ThoiDiem).AsNoTracking().ToList();
    }
}