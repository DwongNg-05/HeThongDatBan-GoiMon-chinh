using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RestaurantManagement.Data.Entities
{
    public class AreaEntity
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên khu vực không được để trống")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 999)]
        public int SortOrder { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}