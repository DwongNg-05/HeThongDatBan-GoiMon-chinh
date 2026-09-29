using System;
using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Entities
{
    public class TaiKhoan
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string TenDangNhap { get; set; } = string.Empty;
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; }

        [Required]
        public string MatKhau { get; set; } = string.Empty;

        public bool BatBuocDoiMatKhau { get; set; } = true;

        public DateTime? ThoiGianDoiMatKhau { get; set; }
    }
}