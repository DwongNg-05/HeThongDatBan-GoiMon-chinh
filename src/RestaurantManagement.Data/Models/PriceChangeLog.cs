using System;

namespace RestaurantManagement.Data.Models
{
    public class PriceChangeLog
    {
        public int Id { get; set; }
        public int DishId { get; set; }
        public string DishName { get; set; } = string.Empty;
        public int OldPriceVnd { get; set; }
        public int NewPriceVnd { get; set; }
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
        public string ChangedBy { get; set; } = string.Empty;
    }
}
