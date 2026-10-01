using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Pages.QuanLyMon;

public class IndexModel : PageModel
{
    public const int SoDongNhatKyGia = 20;

    private readonly IQuanLyMonStore _store;
    private readonly ManagementStore _management;

    public IndexModel(IQuanLyMonStore store, ManagementStore management)
    {
        _store = store;
        _management = management;
    }

    public IEnumerable<MonAn> DanhSachMonAn { get; set; } = Enumerable.Empty<MonAn>();
    public Dictionary<int, string> LookupNhom { get; set; } = new();

    /// <summary>Chỉ Quản lý: món nào đang "tạm hết" hôm nay (theo mã món).</summary>
    public Dictionary<int, bool> TamHet { get; private set; } = new();

    /// <summary>Chỉ Quản lý: các lần đổi giá gần nhất (chỉ xem).</summary>
    public IReadOnlyList<GhiNhanThayDoiGia> NhatKyGia { get; private set; } = [];

    public bool LaQuanLy { get; private set; }

    public async Task OnGetAsync()
    {
        DanhSachMonAn = _store.LayTatCaMonAn();
        LookupNhom = _store.LayTatCaNhomMon().ToDictionary(n => n.Id, n => n.Ten);

        LaQuanLy = User.IsInRole("Manager");
        if (!LaQuanLy) return;
        NhatKyGia = _store.LayNhatKyGiaGanDay(SoDongNhatKyGia).ToList();
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            try
            {
                var menu = await _management.GetMenu(actorId);
                TamHet = menu.Items.ToDictionary(i => i.Id, i => i.IsSoldOut);
            }
            catch (SqlException ex) when (ex.Number == 51001)
            {
                // Quyền bị thu hồi trong phiên: ẩn nút tạm hết, danh sách món vẫn hiển thị.
                LaQuanLy = false;
                NhatKyGia = [];
            }
        }
    }
}
