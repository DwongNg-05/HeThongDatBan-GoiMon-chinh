using System;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;

        [Required]
        public string Action { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;
    }
}