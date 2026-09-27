using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

public class HomeController : Controller
{
    private readonly DemoTableCatalog _tableCatalog;

    public HomeController(DemoTableCatalog tableCatalog) => _tableCatalog = tableCatalog;

    public IActionResult Index()
    {
        return View(_tableCatalog.Get(Request.Query["area"]));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
