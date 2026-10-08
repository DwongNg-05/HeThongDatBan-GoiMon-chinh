using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.Dishes;

// S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
// S1-04 Task 1: quản lý món thuộc nhóm API thực đơn → chặn Phục vụ, chỉ Quản lý.
[Authorize(Roles = AppRoles.Manager)]
public class IndexModel : PageModel
{
    public const int PriceHistoryRows = 20;

    private readonly IMenuStore _store;
    private readonly ManagementStore _management;
    private readonly DailyDishStore? _daily;

    public IndexModel(IMenuStore store, ManagementStore management, DailyDishStore? daily = null)
    {
        _daily = daily;
        _store = store;
        _management = management;
    }

    public IEnumerable<Dish> Dishes { get; set; } = Enumerable.Empty<Dish>();
    public Dictionary<int, string> CategoryNames { get; set; } = new();
    public Dictionary<int, string?> CategoryDefaultImages { get; set; } = new();

    /// <summary>Chỉ Quản lý: món nào đang "tạm hết" hôm nay (theo mã món).</summary>
    public Dictionary<int, bool> SoldOut { get; private set; } = new();

    /// <summary>Chỉ Quản lý: các lần đổi giá gần nhất (chỉ xem).</summary>
    public IReadOnlyList<PriceChangeLog> PriceChanges { get; private set; } = [];

    /// <summary>S2-08 Task 4: món đang "Tạm hết" (Bếp/Quản lý bật ở Món trong ngày) — hiển thị đồng nhất với các màn hình khác.</summary>
    public IReadOnlySet<int> TemporarilyOut { get; private set; } = new HashSet<int>();

    public bool IsManager { get; private set; }

    public async Task OnGetAsync()
    {
        Dishes = _store.GetAllDishes();
        var categories = _store.GetAllCategories().ToList();
        CategoryNames = categories.ToDictionary(n => n.Id, n => n.Name);
        CategoryDefaultImages = categories.ToDictionary(n => n.Id,
            n => n.DefaultImagePath ?? DishImage.DefaultForCategory(n.Name));

        IsManager = User.IsInRole("Manager");
        if (!IsManager) return;
        PriceChanges = _store.GetRecentPriceChanges(PriceHistoryRows).ToList();
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            try
            {
                var menu = await _management.GetMenu(actorId);
                SoldOut = menu.Items.ToDictionary(i => i.Id, i => i.IsSoldOut);
                if (_daily is not null) TemporarilyOut = (await _daily.Availability()).TemporarilyOut.ToHashSet();
            }
            catch (SqlException ex) when (ex.Number == 51001)
            {
                // Quyền bị thu hồi trong phiên: ẩn nút tạm hết, danh sách món vẫn hiển thị.
                IsManager = false;
                PriceChanges = [];
            }
        }
    }
}
