using System.Globalization;
using System.Text;

namespace RestaurantManagement.Web.Services;

/// <summary>
/// S2-01 Task 2: tìm món trên thực đơn công khai theo tên,
/// không phân biệt chữ hoa/chữ thường và không phân biệt dấu tiếng Việt ("com rang" khớp "Cơm rang").
/// </summary>
public static class MenuSearch
{
    /// <summary>Độ dài tối đa của từ khoá; phần dư bị cắt bỏ.</summary>
    public const int MaxKeywordLength = 100;

    /// <summary>
    /// Chuẩn hoá chuỗi để so khớp: bỏ dấu (kể cả "đ"/"Đ" → "d"), chuyển chữ thường,
    /// gộp khoảng trắng liên tiếp thành một dấu cách và bỏ khoảng trắng hai đầu.
    /// Chấp nhận cả chữ dựng sẵn lẫn chữ tổ hợp (NFC/NFD).
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(c switch
            {
                'đ' or 'Đ' => 'd',
                _ => char.ToLowerInvariant(c)
            });
        }
        return builder.ToString();
    }

    /// <summary>Từ khoá người dùng nhập sau khi bỏ khoảng trắng thừa và giới hạn độ dài; rỗng nếu không tìm.</summary>
    public static string CleanKeyword(string? keyword)
    {
        var value = string.Join(' ', (keyword ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length > MaxKeywordLength ? value[..MaxKeywordLength].TrimEnd() : value;
    }

    /// <summary>Tên món có chứa từ khoá (đã chuẩn hoá) hay không. Từ khoá rỗng khớp mọi món.</summary>
    public static bool Matches(string dishName, string? keyword)
    {
        var key = Normalize(keyword);
        return key.Length == 0 || Normalize(dishName).Contains(key, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lọc thực đơn theo tên món. Từ khoá rỗng trả về nguyên thực đơn.
    /// Khi có từ khoá: chỉ giữ nhóm có món khớp, giữ nguyên thứ tự nhóm/món và toàn bộ thông tin món
    /// (nhóm, ảnh, tên, mô tả, giá, đơn vị, cờ hết trong ngày).
    /// </summary>
    public static IReadOnlyList<PublicMenuCategory> Filter(IReadOnlyList<PublicMenuCategory> menu, string? keyword)
    {
        var key = Normalize(CleanKeyword(keyword));
        if (key.Length == 0) return menu;

        return menu
            .Select(category => category with
            {
                Dishes = category.Dishes
                    .Where(dish => Normalize(dish.Name).Contains(key, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(category => category.Dishes.Count > 0)
            .ToArray();
    }
}
