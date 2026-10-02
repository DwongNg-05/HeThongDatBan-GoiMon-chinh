using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Models.Tables;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

/// <summary>Public, anonymous endpoint encoded in printed table QR codes.</summary>
[AllowAnonymous]
[Route("q")]
public sealed class TableQrController(TableQrService qrService) : Controller
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Scan(string token)
    {
        var qr = await qrService.FindByPublicTokenAsync(token);
        if (qr is null || !qr.IsActive)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("NotFound");
        }

        if (qr.IsRevoked)
        {
            Response.StatusCode = StatusCodes.Status410Gone;
            return View("Changed");
        }

        return View("Scan", new TableQrScanViewModel
        {
            TableCode = qr.TableCode,
            AreaName = qr.AreaName,
            MinCapacity = qr.MinCapacity,
            MaxCapacity = qr.MaxCapacity,
            TableType = qr.TableType
        });
    }
}
