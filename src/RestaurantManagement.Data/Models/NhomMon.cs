using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    
    public class NhomMon
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên nhóm món không được để trống.")]
        [StringLength(50, ErrorMessage = "Tên nhóm món không được vượt quá 50 ký tự.")]
        public string Ten { get; set; } = string.Empty;

        public bool DangSuDung { get; set; } = true;

        [Range(0, int.MaxValue)]
        public int ThuTuHienThi { get; set; }
    }
}
