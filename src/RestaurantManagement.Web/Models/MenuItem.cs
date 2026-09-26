namespace RestaurantManagement.Web.Models;

public record MenuItem(int Id, string Name, decimal Price, bool IsSoldOut);
