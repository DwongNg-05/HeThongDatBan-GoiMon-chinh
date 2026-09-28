using System.Collections.Generic;
using System;
using System.Linq;
using RestaurantManagement.Data.Data;
using RestaurantManagement.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace RestaurantManagement.Web.Services
{
    public class SqlQuanLyMonStore : IQuanLyMonStore
    {
        private readonly RestaurantDbContext _db;
        private readonly IHttpContextAccessor _http;

        public SqlQuanLyMonStore(RestaurantDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        public IEnumerable<NhomMon> LayTatCaNhomMon() => _db.NhomMons.OrderBy(n => n.Id).AsNoTracking().ToList();

        public NhomMon AddNhomMon(NhomMon nhom)
        {
            _db.NhomMons.Add(nhom);
            _db.SaveChanges();
            return nhom;
        }

        public IEnumerable<MonAn> LayTatCaMonAn() => _db.MonAns.OrderBy(m => m.Id).AsNoTracking().ToList();

        public MonAn ThemMonAn(MonAn mon)
        {
            _db.MonAns.Add(mon);
            _db.SaveChanges();
            return mon;
        }

        public MonAn? LayMonAn(int id) => _db.MonAns.AsNoTracking().FirstOrDefault(m => m.Id == id);
        // No-op rewrite to trigger file update
        public MonAn? CapNhatMonAn(MonAn updated)
        {
            var existing = _db.MonAns.FirstOrDefault(m => m.Id == updated.Id);
            if (existing == null) return null;

            var oldPrice = existing.GiaBanVnd;
            var newPrice = updated.GiaBanVnd;

            existing.Ten = updated.Ten;
            existing.NhomMonId = updated.NhomMonId;
            existing.GiaBanVnd = updated.GiaBanVnd;
            existing.DonViTinh = updated.DonViTinh;
            existing.MoTaNgan = updated.MoTaNgan;
            existing.ThoiGianCheBienPhut = updated.ThoiGianCheBienPhut;
            existing.TrangThai = updated.TrangThai;

            _db.SaveChanges();

            if (oldPrice != newPrice)
            {
                var user = _http.HttpContext?.User?.Identity?.Name ?? Environment.UserName;
                var entry = new GhiNhanThayDoiGia
                {
                    MonAnId = existing.Id,
                    TenMon = existing.Ten,
                    GiaCuVnd = oldPrice,
                    GiaMoiVnd = newPrice,
                    ThoiDiem = DateTime.UtcNow,
                    NguoiSua = user ?? "(unknown)"
                };
                _db.GhiNhanThayDoiGias.Add(entry);
                _db.SaveChanges();
            }

            return existing;
        }

        public DonHang TaoDonHang()
        {
            var dh = new DonHang { ThoiGianTao = DateTime.UtcNow, IsDangMo = true };
            _db.DonHangs.Add(dh);
            _db.SaveChanges();
            return dh;
        }

        public DonHang? LayDonHang(int donHangId) => _db.DonHangs.AsNoTracking().FirstOrDefault(d => d.Id == donHangId);

        public DongHang ThemDongHang(int donHangId, DongHang dong)
        {
            dong.DonHangId = donHangId;
            _db.DongHangs.Add(dong);
            _db.SaveChanges();
            return dong;
        }

        public IEnumerable<DongHang> LayDongHangTheoDon(int donHangId) => _db.DongHangs.Where(d => d.DonHangId == donHangId).OrderBy(d => d.Id).AsNoTracking().ToList();

        public IEnumerable<DonHang> LayTatCaDonHang() => _db.DonHangs.OrderBy(d => d.Id).AsNoTracking().ToList();

        public IEnumerable<GhiNhanThayDoiGia> LayNhatKyGiaChoMon(int monAnId)
            => _db.GhiNhanThayDoiGias.Where(e => e.MonAnId == monAnId).OrderByDescending(e => e.ThoiDiem).AsNoTracking().ToList();
    }
}
