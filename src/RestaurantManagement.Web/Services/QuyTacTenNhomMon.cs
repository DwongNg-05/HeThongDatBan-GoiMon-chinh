using System.Text;
using System.Text.RegularExpressions;

namespace RestaurantManagement.Web.Services;

public static class QuyTacTenNhomMon
{
    public const string TenRong = "Tên nhóm món không được để trống.";
    public const string TenQuaDai = "Tên nhóm món không được vượt quá 50 ký tự.";
    public const string TenTrung = "Tên nhóm món đã tồn tại. Vui lòng nhập tên khác.";

    public static string ChuanHoa(string? ten) =>
        Regex.Replace((ten ?? "").Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
}
