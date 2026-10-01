using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

// S1-05 Task 3: nhật ký thay đổi giá chỉ dành cho Quản lý, chỉ xem; các phương thức ghi luôn trả 405.
[Authorize(Roles = "Manager")]
public class NhatKyGiaModel : PageModel
{
    private readonly IQuanLyMonStore _store;

    public NhatKyGiaModel(IQuanLyMonStore store)
    {
        _store = store;
    }

    public IEnumerable<GhiNhanThayDoiGia> NhatKy { get; set; } = Enumerable.Empty<GhiNhanThayDoiGia>();
    public MonAn? Mon { get; set; }

    public IActionResult OnGet(int id)
    {
        Mon = _store.LayMonAn(id);
        if (Mon == null) return NotFound();
        NhatKy = _store.LayNhatKyGiaChoMon(id);
        return Page();
    }

    // Chỉ xem: mọi lời gọi ghi trực tiếp tới trang nhật ký đều bị từ chối tường minh (405),
    // thay vì để Razor Pages hiển thị lại trang khi không có handler tương ứng.
    public IActionResult OnPost() => ReadOnly();
    public IActionResult OnPut() => ReadOnly();
    public IActionResult OnDelete() => ReadOnly();
    public IActionResult OnPatch() => ReadOnly();

    private StatusCodeResult ReadOnly() => StatusCode(StatusCodes.Status405MethodNotAllowed);
}
