using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    public class DongHang
    {
        public int Id { get; set; }

        
        public int DonHangId { get; set; }

        
        public string TenMonTaiThoiDiem { get; set; } = string.Empty;

        
        public int GiaBanTaiThoiDiemVnd { get; set; }

        
        public string DonViTinh { get; set; } = string.Empty;

        
        [Range(1, 1000)]
        public int SoLuong { get; set; } = 1;

        
        public long ThanhTien => (long)GiaBanTaiThoiDiemVnd * SoLuong;
    }

    
    public class DonHang
    {
        public int Id { get; set; }

        public DateTime ThoiGianTao { get; set; } = DateTime.UtcNow;

        
        public bool IsDangMo { get; set; } = true;
    }
}
