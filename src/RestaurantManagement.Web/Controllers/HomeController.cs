using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Controllers;

public class HomeController(DemoTableCatalog tableCatalog) : Controller
{
    [Authorize(Roles = "Manager,Waiter")]
    public IActionResult Index()
    {
        return View(tableCatalog.Get(Request.Query["area"]));
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
