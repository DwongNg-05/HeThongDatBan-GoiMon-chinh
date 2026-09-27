using System.Collections.Concurrent;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services
{
    public class InMemoryQuanLyMonStore
    {

        private readonly ConcurrentDictionary<int, MonAn> _monans = new();
        private readonly ConcurrentDictionary<int, NhomMon> _nhommons = new();
        private readonly ConcurrentDictionary<int, DonHang> _donhangs = new();
        private readonly ConcurrentDictionary<int, DongHang> _donghangs = new();
        private readonly ConcurrentDictionary<int, RestaurantManagement.Data.Models.GhiNhanThayDoiGia> _nhatkyGia = new();
        private int _nextGhiNhanId = 0;
        private int _nextMonAnId = 0;
        private int _nextNhomMonId = 0;
        private int _nextDonHangId = 0;
        private int _nextDongHangId = 0;

        public InMemoryQuanLyMonStore()
        {
            AddNhomMon(new NhomMon { Ten = "Khai vị" });
            AddNhomMon(new NhomMon { Ten = "Món chính" });
            AddNhomMon(new NhomMon { Ten = "Tráng miệng" });
        }

      
        public IEnumerable<NhomMon> LayTatCaNhomMon() => _nhommons.Values.OrderBy(n => n.Id);

        // no external DB integration in the in-memory store

    
        public NhomMon AddNhomMon(NhomMon nhom)
        {
            var id = System.Threading.Interlocked.Increment(ref _nextNhomMonId);
            nhom.Id = id;
            _nhommons.TryAdd(nhom.Id, nhom);
            return nhom;
        }

      
        public IEnumerable<MonAn> LayTatCaMonAn() => _monans.Values.OrderBy(m => m.Id);

        public MonAn ThemMonAn(MonAn mon)
        {
            var id = System.Threading.Interlocked.Increment(ref _nextMonAnId);
            mon.Id = id;
            _monans.TryAdd(mon.Id, mon);
            return mon;
        }

        // Lấy món theo id
        public MonAn? LayMonAn(int id) => _monans.TryGetValue(id, out var mon) ? mon : null;

        // Cập nhật món (thay đổi giá, tên, mô tả, trạng thái...).
        // Lưu ý: không chạm vào các DongHang đã lưu - chúng đã snapshot giá tại thời điểm gọi.
        public MonAn? CapNhatMonAn(MonAn updated)
        {
            if (!_monans.ContainsKey(updated.Id)) return null;
            // retrieve existing for comparison
            var existing = _monans[updated.Id];

            var oldPrice = existing.GiaBanVnd;
            var newPrice = updated.GiaBanVnd;

            // Replace atomically and update fields
            _monans.AddOrUpdate(updated.Id, updated, (key, old) =>
            {
                old.Ten = updated.Ten;
                old.NhomMonId = updated.NhomMonId;
                old.GiaBanVnd = updated.GiaBanVnd;
                old.DonViTinh = updated.DonViTinh;
                old.MoTaNgan = updated.MoTaNgan;
                old.ThoiGianCheBienPhut = updated.ThoiGianCheBienPhut;
                old.TrangThai = updated.TrangThai;
                return old;
            });

            // If price changed, record a journal entry
            if (oldPrice != newPrice)
            {
                var gid = System.Threading.Interlocked.Increment(ref _nextGhiNhanId);
                var entry = new RestaurantManagement.Data.Models.GhiNhanThayDoiGia
                {
                    Id = gid,
                    MonAnId = updated.Id,
                    TenMon = updated.Ten,
                    GiaCuVnd = oldPrice,
                    GiaMoiVnd = newPrice,
                    ThoiDiem = DateTime.UtcNow,
                    NguoiSua = GetCurrentUserName() ?? "(không rõ)"
                };
                _nhatkyGia.TryAdd(entry.Id, entry);
            }

            return _monans[updated.Id];
        }

        // Lấy nhật ký thay đổi giá cho một món theo thời gian giảm dần (gần nhất trước)
        public IEnumerable<RestaurantManagement.Data.Models.GhiNhanThayDoiGia> LayNhatKyGiaChoMon(int monAnId)
            => _nhatkyGia.Values.Where(e => e.MonAnId == monAnId).OrderByDescending(e => e.ThoiDiem);

        // Very small helper to resolve current user - in this demo read from environment thread principal if available
        private string? GetCurrentUserName()
        {
            try
            {
                var name = System.Security.Claims.ClaimsPrincipal.Current?.Identity?.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return Environment.UserName;
        }

        // Tạo đơn hàng mới và trả về DonHang
        public DonHang TaoDonHang()
        {
            var id = System.Threading.Interlocked.Increment(ref _nextDonHangId);
            var dh = new DonHang { Id = id, ThoiGianTao = DateTime.UtcNow, IsDangMo = true };
            _donhangs.TryAdd(dh.Id, dh);
            return dh;
        }

        // Lấy đơn hàng theo id
        public DonHang? LayDonHang(int donHangId) => _donhangs.TryGetValue(donHangId, out var dh) ? dh : null;

        // Thêm dòng hàng vào đơn: lưu snapshot tên, giá, đơn vị
        public DongHang ThemDongHang(int donHangId, DongHang dong)
        {
            var id = System.Threading.Interlocked.Increment(ref _nextDongHangId);
            dong.Id = id;
            dong.DonHangId = donHangId;
            _donghangs.TryAdd(dong.Id, dong);
            return dong;
        }

        // Lấy các dòng hàng của một đơn
        public IEnumerable<DongHang> LayDongHangTheoDon(int donHangId) => _donghangs.Values.Where(d => d.DonHangId == donHangId).OrderBy(d => d.Id);

        // Lấy tất cả đơn hàng (dùng cho demo/admin)
        public IEnumerable<DonHang> LayTatCaDonHang() => _donhangs.Values.OrderBy(d => d.Id);
    }
}
