using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.Dishes;

public class IndexModel : PageModel
{
    public const int PriceHistoryRows = 20;

    private readonly IMenuStore _store;
    private readonly ManagementStore _management;

    public IndexModel(IMenuStore store, ManagementStore management)
    {
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
