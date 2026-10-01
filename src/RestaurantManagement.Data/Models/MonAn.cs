using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Data.Models
{
    // Trạng thái món
    public enum TrangThaiMon
    {
        DangBan,
        NgungBan
    }

    // Lớp thực thể MonAn chứa thông tin món ăn
    public class MonAn
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên món không được để trống")]
        public string Ten { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nhóm món là bắt buộc")]
        public int NhomMonId { get; set; }

        // Giá bán tính bằng VND, kiểu số nguyên
        [Required(ErrorMessage = "Giá bán là bắt buộc")]
        [Range(1, 50_000_000, ErrorMessage = "Giá bán phải là số nguyên dương và <= 50.000.000 VND")]
        public int GiaBanVnd { get; set; }

        // Đơn vị tính cho phép nhập tự do
        [Required(ErrorMessage = "Đơn vị tính là bắt buộc")]
        public string DonViTinh { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Mô tả ngắn không quá 500 ký tự")]
        public string MoTaNgan { get; set; } = string.Empty;

        // Thời gian ước tính chế biến (phút)
        [Required(ErrorMessage = "Thời gian chế biến là bắt buộc")]
        [Range(1, 10_000, ErrorMessage = "Thời gian chế biến phải là số phút hợp lệ")]
        public int ThoiGianCheBienPhut { get; set; }

        public TrangThaiMon TrangThai { get; set; } = TrangThaiMon.DangBan;

        // Đường dẫn ảnh món (cột MenuItems.ImagePath). Để trống thì dùng ảnh mặc định của nhóm món.
        [StringLength(500, ErrorMessage = "Đường dẫn ảnh không quá 500 ký tự")]
        public string? DuongDanAnh { get; set; }
    }
}
