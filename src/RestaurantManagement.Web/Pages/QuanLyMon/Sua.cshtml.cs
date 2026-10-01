using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

[RequestFormLimits(MultipartBodyLengthLimit = QuyTacAnhMonAn.KichThuocToiDa + 1024 * 1024)]
[RequestSizeLimit(QuyTacAnhMonAn.KichThuocToiDa + 1024 * 1024)]
public class SuaModel : PageModel
{
    private readonly IQuanLyMonStore _store;
    private readonly IKhoAnhMonAn _khoAnh;

    public SuaModel(IQuanLyMonStore store, IKhoAnhMonAn khoAnh)
    {
        _store = store;
        _khoAnh = khoAnh;
    }

    [BindProperty]
    public MonAn Mon { get; set; } = new MonAn();

    /// <summary>Ảnh mới (JPG/PNG, tối đa 5 MB). Bỏ trống thì giữ ảnh hiện tại.</summary>
    [BindProperty]
    public IFormFile? AnhMon { get; set; }

    /// <summary>Ảnh đang hiển thị của món (ảnh riêng hoặc ảnh mặc định).</summary>
    public string AnhHienTai { get; private set; } = AnhMonAn.AnhMacDinh;

    public IEnumerable<NhomMon> DanhSachNhom { get; set; } = Enumerable.Empty<NhomMon>();

    public IActionResult OnGet(int id)
    {
        DanhSachNhom = _store.LayTatCaNhomMon();
        var mon = _store.LayMonAn(id);
        if (mon == null) return NotFound();
        Mon = new MonAn
        {
            Id = mon.Id,
            Ten = mon.Ten,
            NhomMonId = mon.NhomMonId,
            GiaBanVnd = mon.GiaBanVnd,
            DonViTinh = mon.DonViTinh,
            MoTaNgan = mon.MoTaNgan,
            ThoiGianCheBienPhut = mon.ThoiGianCheBienPhut,
            TrangThai = mon.TrangThai,
            DuongDanAnh = mon.DuongDanAnh
        };
        AnhHienTai = AnhMonAn.ChonAnh(mon.DuongDanAnh);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        DanhSachNhom = _store.LayTatCaNhomMon();
        ModelState.Remove("Mon.DuongDanAnh");
        var existing = _store.LayMonAn(Mon.Id);
        if (existing == null) return NotFound();
        // Ảnh cũ lấy từ dữ liệu đã lưu, không nhận đường dẫn ảnh từ biểu mẫu.
        var anhCu = existing.DuongDanAnh;
        AnhHienTai = AnhMonAn.ChonAnh(anhCu);

        // Reject unauthenticated saves before the store throws an exception.
        var currentUser = HttpContext.RequestServices.GetRequiredService<RestaurantManagement.Web.Security.ICurrentUser>();
        if (!currentUser.IsAuthenticated)
        {
            ModelState.AddModelError(string.Empty, "Chưa đăng nhập nên không thể lưu thay đổi. Vui lòng đăng nhập bằng tài khoản có quyền sửa món.");
            return Page();
        }

        if (!DanhSachNhom.Any(n => n.Id == Mon.NhomMonId))
            ModelState.AddModelError("Mon.NhomMonId", "Nhóm món không tồn tại.");

        if (AnhMon is not null)
        {
            var ketQua = QuyTacAnhMonAn.KiemTra(AnhMon);
            if (!ketQua.HopLe) ModelState.AddModelError(nameof(AnhMon), ketQua.Loi!);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        string? anhMoi = null;
        try
        {
            if (AnhMon is not null)
                anhMoi = await _khoAnh.LuuAsync(AnhMon, cancellationToken);
            Mon.DuongDanAnh = anhMoi ?? anhCu;   // đường dẫn ảnh ghi vào MenuItems.ImagePath
            _store.CapNhatMonAn(Mon);
        }
        catch (ValidationException ex)
        {
            _khoAnh.Xoa(anhMoi);
            ModelState.AddModelError(nameof(AnhMon), ex.Message);
            return Page();
        }
        catch (UnauthorizedAccessException)
        {
            _khoAnh.Xoa(anhMoi);
            ModelState.AddModelError(string.Empty, "Phiên đăng nhập không còn hợp lệ. Vui lòng đăng nhập lại trước khi lưu.");
            return Page();
        }
        catch (ArgumentException)
        {
            _khoAnh.Xoa(anhMoi);
            ModelState.AddModelError("Mon.NhomMonId", "Nhóm món không còn tồn tại. Vui lòng chọn nhóm khác.");
            DanhSachNhom = _store.LayTatCaNhomMon();
            return Page();
        }
        catch
        {
            _khoAnh.Xoa(anhMoi);
            throw;
        }

        // Đã lưu ảnh mới thành công: dọn ảnh tải lên cũ (ảnh mẫu trong /images không bị xoá).
        if (anhMoi is not null && anhCu != anhMoi)
            _khoAnh.Xoa(anhCu);

        return RedirectToPage("/QuanLyMon/Index");
    }
}
