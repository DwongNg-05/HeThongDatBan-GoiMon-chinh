using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services;

public sealed record CategoryWithDishes(int Id, string Name, IReadOnlyList<Dish> Dishes);
