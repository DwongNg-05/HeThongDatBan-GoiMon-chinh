using Microsoft.AspNetCore.Authorization;
using RestaurantManagement.Web.Security;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

// S1-04 Task 4: kiểm tra quyền ở máy chủ theo vai trò (docs/S1-04-Task4.md).
// S1-04 Task 2: sơ đồ bàn là việc của Quản lý và Phục vụ; Bếp, Thu ngân mở "/" được đưa tới màn hình của mình (GuestMenuEntry).
[Authorize(Roles = AppRoles.FrontOfHouse)]
public class HomeController : Controller
{
    private readonly TableMapStore _tableMap;
    private readonly ILogger<HomeController> _logger;

    public HomeController(TableMapStore tableMap, ILogger<HomeController> logger)
    {
        _tableMap = tableMap;
        _logger = logger;
    }

    // Sơ đồ bàn lấy khu vực và bàn từ màn hình "Khu vực & bàn" (dbo.Areas, dbo.DiningTables).
    public async Task<IActionResult> Index(int? areaId, CancellationToken cancellationToken)
    {
        try
        {
            return View(await _tableMap.GetMapAsync(areaId, cancellationToken));
        }
        catch (Exception exception) when (exception is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            _logger.LogError(exception, "Không đọc được dữ liệu sơ đồ bàn từ SQL Server.");
            return View(new TableMapViewModel
            {
                Tables = [],
                Areas = [],
                LoadError = "Không tải được dữ liệu khu vực và bàn. Vui lòng kiểm tra kết nối cơ sở dữ liệu rồi tải lại trang."
            });
        }
    }

    // S1-04 Task 1: trang thông tin không thuộc phần việc của Phục vụ (chỉ sơ đồ bàn, đặt bàn, gọi món).
    [Authorize(Roles = AppRoles.Manager)]
    public IActionResult Privacy()
    {
        return View();
    }

    // Trang báo lỗi chung: ai cũng xem được (không chứa dữ liệu nghiệp vụ).
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
