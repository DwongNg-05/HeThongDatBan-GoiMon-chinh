using System;

namespace RestaurantManagement.Data.Models
{
    public class GhiNhanThayDoiGia
    {
        public int Id { get; set; }
        public int MonAnId { get; set; }
        public string TenMon { get; set; } = string.Empty;
        public int GiaCuVnd { get; set; }
        public int GiaMoiVnd { get; set; }
        public DateTime ThoiDiem { get; set; } = DateTime.UtcNow;
        public string NguoiSua { get; set; } = string.Empty;
    }
}
