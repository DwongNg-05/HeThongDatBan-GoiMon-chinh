using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Username { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        [Required]
        [StringLength(30)]
        public string Role { get; set; } = "Nhân viên";

        public bool IsActive { get; set; } = true;
    }
}