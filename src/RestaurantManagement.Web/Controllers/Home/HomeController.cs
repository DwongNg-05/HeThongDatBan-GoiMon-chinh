using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

public class HomeController : Controller
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult Index()
    {
        return RedirectToAction("Index", User.IsInRole("Manager") || User.IsInRole("Waiter") ? "TableMap" : "Menu");
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
