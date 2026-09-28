using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RestaurantManagement.Data.Entities
{
    public class KhuVuc
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên khu vực không được để trống")]
        [StringLength(100)]
        public string TenKhuVuc { get; set; } = string.Empty;

        [Range(0, 999)]
        public int ThuTuHienThi { get; set; }

        [StringLength(500)]
        public string? GhiChu { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}