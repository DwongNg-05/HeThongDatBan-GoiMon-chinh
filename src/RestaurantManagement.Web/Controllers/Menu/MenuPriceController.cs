using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Security;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[ApiController]
[Route("api/menu")]
[Authorize(Roles = AppRoles.Manager)]
public sealed class MenuPriceController(IMenuStore store) : ControllerBase
{
    [HttpPut("{id:int}/price")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult UpdatePrice(int id, [FromBody] UpdateMenuPriceRequest request)
    {
        try
        {
            var dish = store.GetDish(id);
            if (dish is null) return NotFound(new { message = "Không tìm thấy món." });

            dish.PriceVnd = request.PriceVnd;
            var updated = store.UpdateDish(dish);
            return updated is null
                ? NotFound(new { message = "Không tìm thấy món." })
                : Ok(new { updated.Id, updated.Name, updated.PriceVnd });
        }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            return Forbid();
        }
        catch (SqlException)
        {
            return Problem("Không thể cập nhật giá món lúc này.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

public sealed record UpdateMenuPriceRequest([Required, Range(1, 50_000_000)] int PriceVnd);
