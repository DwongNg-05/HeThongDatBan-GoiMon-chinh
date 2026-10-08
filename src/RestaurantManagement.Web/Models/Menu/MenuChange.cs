namespace RestaurantManagement.Web.Models;

public record MenuChange(DateTime ChangedAt, string UserName, string Name, string Description);
