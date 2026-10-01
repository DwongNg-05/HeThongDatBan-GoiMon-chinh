using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

[RequestFormLimits(MultipartBodyLengthLimit = QuyTacAnhMonAn.KichThuocToiDa + 1024 * 1024)]
[RequestSizeLimit(QuyTacAnhMonAn.KichThuocToiDa + 1024 * 1024)]
public class TaoModel : PageModel
{
    private readonly IQuanLyMonStore _store;
    private readonly IKhoAnhMonAn _khoAnh;

    public TaoModel(IQuanLyMonStore store, IKhoAnhMonAn khoAnh)
    {
        _store = store;
        _khoAnh = khoAnh;
    }

    [BindProperty]
    public MonAn Mon { get; set; } = new MonAn();

    /// <summary>Ảnh món (JPG/PNG, tối đa 5 MB). Không bắt buộc: bỏ trống thì dùng ảnh mặc định của nhóm.</summary>
    [BindProperty]
    public IFormFile? AnhMon { get; set; }

    public IEnumerable<NhomMon> DanhSachNhom { get; set; } = Enumerable.Empty<NhomMon>();

    public void OnGet()
    {
        DanhSachNhom = _store.LayTatCaNhomMon();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        DanhSachNhom = _store.LayTatCaNhomMon();

        // Đường dẫn ảnh chỉ do máy chủ tạo ra, không nhận từ biểu mẫu.
        Mon.DuongDanAnh = null;
        ModelState.Remove("Mon.DuongDanAnh");

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
            Mon.DuongDanAnh = anhMoi;   // ghi đường dẫn ảnh vào MenuItems.ImagePath
            _store.ThemMonAn(Mon);
        }
        catch (ValidationException ex)
        {
            _khoAnh.Xoa(anhMoi);
            ModelState.AddModelError(nameof(AnhMon), ex.Message);
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
        return RedirectToPage("/QuanLyMon/Index");
    }
}
