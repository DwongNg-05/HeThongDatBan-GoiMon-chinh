using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services;

public sealed record NhomMonThucDon(int Id, string Ten, IReadOnlyList<MonAn> MonAn);
