using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RestaurantManagement.Web.Models.Reservations;

public class NoShowWarningInput
{
    public bool NoShowAcknowledged { get; set; }
    public string? NoShowWarningToken { get; set; }
    [BindNever] public int NoShowCount { get; set; }
}
