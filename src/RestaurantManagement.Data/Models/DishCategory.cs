using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    
    public class DishCategory
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên nhóm món không được để trống.")]
        [StringLength(50, ErrorMessage = "Tên nhóm món không được vượt quá 50 ký tự.")]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        [Range(0, int.MaxValue)]
        public int SortOrder { get; set; }
    }
}
