using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services;

public sealed record CategoryWithDishes(int Id, string Name, string? DefaultImagePath, IReadOnlyList<Dish> Dishes);
