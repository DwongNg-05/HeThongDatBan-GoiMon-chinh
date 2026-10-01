using System;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Entities
{
    public class AccountEntity
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; }

        [Required]
        public string Password { get; set; } = string.Empty;

        public bool MustChangePassword { get; set; } = true;

        public DateTime? PasswordChangedAt { get; set; }
    }
}