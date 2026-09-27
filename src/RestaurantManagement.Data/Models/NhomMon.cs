using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    
    public class NhomMon
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên nhóm không được để trống")]
        public string Ten { get; set; } = string.Empty;
    }
}
