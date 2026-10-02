using System;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        [StringLength(100)]
        public string Username { get; set; } = "";

        [Required]
        [StringLength(50)]
        public string Role { get; set; } = "";

        [Required]
        [StringLength(100)]
        public string Action { get; set; } = "";

        [StringLength(45)]
        public string IpAddress { get; set; } = "";
    }
}