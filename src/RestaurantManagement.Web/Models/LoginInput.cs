using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models;

public sealed class LoginInput
{
    [Required, StringLength(50)]
    public string UserName { get; set; } = "";

    [Required, StringLength(200)]
    public string Password { get; set; } = "";
}
