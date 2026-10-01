using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.Dishes;

// S1-05 Task 3: nhật ký thay đổi giá chỉ dành cho Quản lý, chỉ xem; các phương thức ghi luôn trả 405.
[Authorize(Roles = "Manager")]
public class PriceHistoryModel : PageModel
{
    private readonly IMenuStore _store;

    public PriceHistoryModel(IMenuStore store)
    {
        _store = store;
    }

    public IEnumerable<PriceChangeLog> Entries { get; set; } = Enumerable.Empty<PriceChangeLog>();
    public Dish? Dish { get; set; }

    public IActionResult OnGet(int id)
    {
        Dish = _store.GetDish(id);
        if (Dish == null) return NotFound();
        Entries = _store.GetPriceHistory(id);
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
