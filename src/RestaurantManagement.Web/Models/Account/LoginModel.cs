using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models;

public class LoginModel
{
    [Required, StringLength(50)]
    public string Identifier { get; set; } = "";
    [Required, StringLength(72)]
    public string Password { get; set; } = "";
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public int RemainingSeconds { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool SessionExpired { get; set; }
}
