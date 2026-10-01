using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services
{
    public class InMemoryQuanLyMonStore : IQuanLyMonStore
    {

        private readonly ConcurrentDictionary<int, MonAn> _monans = new();
        private readonly ConcurrentDictionary<int, NhomMon> _nhommons = new();
        private readonly ConcurrentDictionary<int, DonHang> _donhangs = new();
        private readonly ConcurrentDictionary<int, DongHang> _donghangs = new();
        private readonly ConcurrentDictionary<int, RestaurantManagement.Data.Models.GhiNhanThayDoiGia> _nhatkyGia = new();
        private int _nextGhiNhanId = 0;
        private int _nextMonAnId = 0;
        private int _nextNhomMonId = 0;
        private readonly object _nhomMonLock = new();
        private int _nextDonHangId = 0;
        private int _nextDongHangId = 0;

        public InMemoryQuanLyMonStore()
        {
            var defaults = new[]
            {
                ("Khai vị", "Gỏi cuốn", 45000, "Đĩa", "Tôm, thịt, bún và rau sống cuốn bánh tráng, chấm tương đậu.", "/images/thuc-don/khai-vi.svg"),
                ("Món chính", "Cơm chiên hải sản", 85000, "Đĩa", "Cơm chiên tôm mực, trứng và rau củ, đảo lửa lớn.", "/images/thuc-don/mon-chinh.svg"),
                ("Lẩu", "Lẩu Thái", 250000, "Nồi", "Nước lẩu chua cay, hải sản tươi, nấm và rau ăn kèm cho 2–3 người.", "/images/thuc-don/lau.svg"),
                ("Tráng miệng", "Chè hạt sen", 30000, "Chén", "Hạt sen bùi, nước đường phèn thanh mát.", "/images/thuc-don/trang-mieng.svg"),
                ("Đồ uống", "Trà đào", 35000, "Ly", "Trà đen ủ lạnh với đào miếng và sả.", "/images/thuc-don/do-uong.svg")
            };
            for (var index = 0; index < defaults.Length; index++)
            {
                var (tenNhom, tenMon, gia, donVi, moTa, anh) = defaults[index];
                var nhom = AddNhomMon(new NhomMon { Ten = tenNhom, ThuTuHienThi = index + 1 });
                ThemMonAn(new MonAn
                {
                    Ten = tenMon, NhomMonId = nhom.Id, GiaBanVnd = gia,
                    DonViTinh = donVi, ThoiGianCheBienPhut = 15,
                    MoTaNgan = moTa, DuongDanAnh = anh
                });
            }
        }

      
        public IEnumerable<NhomMon> LayTatCaNhomMon()
        {
            lock (_nhomMonLock)
                return _nhommons.Values.OrderBy(n => n.ThuTuHienThi).ThenBy(n => n.Id).ToArray();
        }

        public void LuuThuTuNhomMon(IReadOnlyList<int> ids, IReadOnlyList<int> banDau)
        {
            lock (_nhomMonLock)
            {
                var current = LayTatCaNhomMon().Select(n => n.Id).ToArray();
                if (!current.SequenceEqual(banDau))
                    throw new ValidationException("Danh sách hoặc thứ tự nhóm món đã thay đổi. Vui lòng tải lại trang và sắp xếp lại.");
                if (ids.Count != current.Length || ids.Distinct().Count() != ids.Count || !ids.ToHashSet().SetEquals(current))
                    throw new ValidationException("Thứ tự không hợp lệ: cần đủ mỗi nhóm món đúng một lần.");
                for (var i = 0; i < ids.Count; i++)
                {
                    var nhom = _nhommons[ids[i]];
                    _nhommons[ids[i]] = new NhomMon
                    {
                        Id = nhom.Id, Ten = nhom.Ten, DangSuDung = nhom.DangSuDung, ThuTuHienThi = i + 1
                    };
                }
            }
        }

        public IEnumerable<NhomMon> LayNhomMonDangSuDung() => LayTatCaNhomMon().Where(n => n.DangSuDung);

        public IEnumerable<MonAn> LayMonAnTheoNhom(int nhomMonId) =>
            LayTatCaMonAn().Where(m => m.NhomMonId == nhomMonId);

        public IReadOnlyList<NhomMonThucDon> LayThucDonTheoNhom()
        {
            var monDangBan = LayTatCaMonAn().Where(m => m.TrangThai == TrangThaiMon.DangBan)
                .ToLookup(m => m.NhomMonId);
            return LayNhomMonDangSuDung()
                .Select(n => new NhomMonThucDon(n.Id, n.Ten, monDangBan[n.Id].ToArray())).ToArray();
        }

        // Store bộ nhớ không theo dõi "hết trong ngày" nên mọi món đang bán đều HetTrongNgay = false.
        public IReadOnlyList<NhomThucDonCongKhai> LayThucDonCongKhai() =>
            ThucDonCongKhaiBuilder.Tao(
                LayNhomMonDangSuDung().Select(n => (n.Id, n.Ten)),
                LayTatCaMonAn().Where(m => m.TrangThai == TrangThaiMon.DangBan)
                    .Select(m => new MonDangBanTho(m.Id, m.NhomMonId, m.Ten, m.MoTaNgan, m.GiaBanVnd,
                        m.DonViTinh, m.DuongDanAnh, 0, false)));

        // no external DB integration in the in-memory store

    
        public NhomMon AddNhomMon(NhomMon nhom)
        {
            lock (_nhomMonLock)
            {
                var ten = KiemTraTenNhom(nhom.Ten);
                if (nhom.ThuTuHienThi < 0)
                    throw new ValidationException("Thứ tự hiển thị phải là số nguyên không âm.");
                nhom.Id = ++_nextNhomMonId;
                nhom.Ten = ten;
                _nhommons[nhom.Id] = nhom;
                return nhom;
            }
        }

        public NhomMon? LayNhomMon(int id) => _nhommons.GetValueOrDefault(id);

        public bool SuaTenNhomMon(int id, string? ten)
        {
            lock (_nhomMonLock)
            {
                if (!_nhommons.TryGetValue(id, out var nhom)) return false;
                var tenHopLe = KiemTraTenNhom(ten, id);
                _nhommons[id] = new NhomMon
                {
                    Id = id, Ten = tenHopLe,
                    DangSuDung = nhom.DangSuDung, ThuTuHienThi = nhom.ThuTuHienThi
                };
                return true;
            }
        }

        public bool DatTrangThaiNhomMon(int id, bool dangSuDung)
        {
            lock (_nhomMonLock)
            {
                if (!_nhommons.TryGetValue(id, out var nhom)) return false;
                _nhommons[id] = new NhomMon
                {
                    Id = id, Ten = nhom.Ten, ThuTuHienThi = nhom.ThuTuHienThi, DangSuDung = dangSuDung
                };
                return true;
            }
        }

        public bool XoaNhomMon(int id)
        {
            lock (_nhomMonLock)
            {
                if (!_nhommons.ContainsKey(id)) return false;
                var count = _monans.Values.Count(m => m.NhomMonId == id);
                if (count > 0)
                    throw new ValidationException($"Không thể xóa nhóm đang chứa {count} món ăn. Vui lòng chuyển toàn bộ món sang nhóm khác trước khi xóa.");
                _nhommons.TryRemove(id, out _);
                var remaining = LayTatCaNhomMon().Select(n => n.Id).ToArray();
                LuuThuTuNhomMon(remaining, remaining);
                return true;
            }
        }

        private string KiemTraTenNhom(string? ten, int? exceptId = null)
        {
            var normalized = QuyTacTenNhomMon.ChuanHoa(ten);
            if (normalized.Length == 0) throw new ValidationException(QuyTacTenNhomMon.TenRong);
            if (normalized.Length > 50) throw new ValidationException(QuyTacTenNhomMon.TenQuaDai);
            if (_nhommons.Values.Any(n => n.Id != exceptId &&
                string.Equals(QuyTacTenNhomMon.ChuanHoa(n.Ten), normalized, StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException(QuyTacTenNhomMon.TenTrung);
            return normalized;
        }

        public IEnumerable<MonAn> LayTatCaMonAn() => _monans.Values.OrderBy(m => m.Id);

        public MonAn ThemMonAn(MonAn mon)
        {
            lock (_nhomMonLock)
            {
                KiemTraNhomMon(mon.NhomMonId);
                var id = System.Threading.Interlocked.Increment(ref _nextMonAnId);
                mon.Id = id;
                _monans.TryAdd(mon.Id, mon);
                return mon;
            }
        }

        // Lấy món theo id
        public MonAn? LayMonAn(int id) => _monans.TryGetValue(id, out var mon) ? mon : null;

        // Cập nhật món (thay đổi giá, tên, mô tả, trạng thái...).
        // Lưu ý: không chạm vào các DongHang đã lưu - chúng đã snapshot giá tại thời điểm gọi.
        public MonAn? CapNhatMonAn(MonAn updated)
        {
            lock (_nhomMonLock)
            {
                if (!_monans.ContainsKey(updated.Id)) return null;
                KiemTraNhomMon(updated.NhomMonId);
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
                    old.DuongDanAnh = updated.DuongDanAnh;
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

        private void KiemTraNhomMon(int nhomMonId)
        {
            if (!_nhommons.ContainsKey(nhomMonId))
                throw new ArgumentException("Nhóm món không tồn tại.", nameof(nhomMonId));
        }
    }
}
