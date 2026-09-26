namespace RestaurantManagement.Web.Models;

public sealed record LoginResult(LoginUser? User, int RemainingSeconds = 0);
