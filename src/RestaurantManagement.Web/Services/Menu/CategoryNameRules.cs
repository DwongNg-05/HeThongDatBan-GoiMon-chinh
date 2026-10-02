using System.Text;
using System.Text.RegularExpressions;

namespace RestaurantManagement.Web.Services;

public static class CategoryNameRules
{
    public const string EmptyName = "Tên nhóm món không được để trống.";
    public const string NameTooLong = "Tên nhóm món không được vượt quá 50 ký tự.";
    public const string DuplicateName = "Tên nhóm món đã tồn tại. Vui lòng nhập tên khác.";

    public static string Normalize(string? name) =>
        Regex.Replace((name ?? "").Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
}
