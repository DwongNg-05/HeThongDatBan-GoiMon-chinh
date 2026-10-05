using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    public class AuditLog
    {
        public long Id { get; set; }

        public int? ActorUserId { get; set; }

        [StringLength(20)]
        public string? ActorRole { get; set; }

        [Required]
        [StringLength(80)]
        public string Action { get; set; } = string.Empty;

        [Required]
        [StringLength(60)]
        public string EntityType { get; set; } = string.Empty;

        [StringLength(60)]
        public string? EntityId { get; set; }

        public string? OldValues { get; set; }

        public string? NewValues { get; set; }

        [StringLength(500)]
        public string? Reason { get; set; }

        [StringLength(45)]
        public string? IpAddress { get; set; }

        public DateTime OccurredAt { get; set; }
    }
}