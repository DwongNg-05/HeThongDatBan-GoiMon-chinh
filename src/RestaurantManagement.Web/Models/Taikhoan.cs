
using Microsoft.AspNetCore.Identity;

namespace RestaurantManagement.Web.Models
{
    public class TaiKhoan : IdentityUser
    {
        // Tài khoản có bắt buộc đổi mật khẩu không
        public bool BatBuocDoiMatKhau { get; set; } = true;

        // Thời điểm đổi mật khẩu gần nhất
        public DateTime? ThoiGianDoiMatKhau { get; set; }
    }
}