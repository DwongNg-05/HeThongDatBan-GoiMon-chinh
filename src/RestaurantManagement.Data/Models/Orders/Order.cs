using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    public class OrderLine
    {
        public int Id { get; set; }

        
        public int OrderId { get; set; }

        
        public string DishNameSnapshot { get; set; } = string.Empty; // alignment

        
        public int UnitPriceVnd { get; set; }

        
        public string Unit { get; set; } = string.Empty;

        
        [Range(1, 1000)]
        public int Quantity { get; set; } = 1;

        
        public long LineTotal => (long)UnitPriceVnd * Quantity;
    }

    
    public class Order
    {
        public int Id { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        
        public bool IsOpen { get; set; } = true;
    }
}
