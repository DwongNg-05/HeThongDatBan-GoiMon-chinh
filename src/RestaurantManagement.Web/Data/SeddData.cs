
using Microsoft.AspNetCore.Identity;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Data
{
    public static class SeedData
    {
        public static async Task TaoTaiKhoanNhanVienAsync(
            UserManager<TaiKhoan> userManager)
        {
            string tenDangNhap = "nhanvien01";
            string matKhauTam = "Tam12345";

            var taiKhoan = await userManager.FindByNameAsync(tenDangNhap);

            if (taiKhoan == null)
            {
                taiKhoan = new TaiKhoan
                {
                    UserName = tenDangNhap,
                    Email = "nhanvien01@example.com",
                    EmailConfirmed = true,
                    BatBuocDoiMatKhau = true
                };

                var taoMoi = await userManager.CreateAsync(
                    taiKhoan, matKhauTam);

                if (!taoMoi.Succeeded)
                {
                    var loi = string.Join(
                        "; ",
                        taoMoi.Errors.Select(e => e.Description));

                    throw new Exception($"Không thể tạo tài khoản: {loi}");
                }
            }
            else
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(
                    taiKhoan);

                var ketQua = await userManager.ResetPasswordAsync(
                    taiKhoan, token, matKhauTam);

                if (!ketQua.Succeeded)
                {
                    var loi = string.Join(
                        "; ",
                        ketQua.Errors.Select(e => e.Description));

                    throw new Exception($"Không thể đặt lại mật khẩu: {loi}");
                }

                taiKhoan.BatBuocDoiMatKhau = true;

                var capNhat = await userManager.UpdateAsync(taiKhoan);

                if (!capNhat.Succeeded)
                {
                    throw new Exception("Không thể cập nhật trạng thái tài khoản.");
                }
            }
        }
    }
}