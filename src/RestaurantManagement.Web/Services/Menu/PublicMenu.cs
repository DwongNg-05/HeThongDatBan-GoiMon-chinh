using System.Globalization;

namespace RestaurantManagement.Web.Services;

/// <summary>Một món trên thực đơn công khai. Chỉ chứa dữ liệu khách được xem.</summary>
public sealed record PublicMenuDish(
    int Id,
    string Name,
    string ShortDescription,
    int PriceVnd,
    string Unit,
    string ImageUrl,
    bool SoldOutToday)
{
    /// <summary>Giá hiển thị theo chuẩn Việt Nam, ví dụ "45.000 ₫".</summary>
    public string DisplayPrice => MoneyFormat.Vnd(PriceVnd);

    /// <summary>
    /// S2-01 Task 3: món đang bán nhưng đã hết trong ngày nghiệp vụ hiện tại.
    /// Món vẫn nằm trong thực đơn (và kết quả tìm kiếm), chỉ hiển thị nhãn "Tạm hết" và bị làm mờ.
    /// </summary>
    public bool IsTemporarilyUnavailable => SoldOutToday;

    /// <summary>Nhãn trạng thái hiển thị cho khách; rỗng khi món còn phục vụ.</summary>
    public string AvailabilityLabel => SoldOutToday ? SoldOutDisplay.Label : string.Empty;

    /// <summary>
    /// S2-01 Task 4: mô tả rút gọn cho trang thực đơn. Trên điện thoại chỉ hiện tối đa 3 dòng,
    /// nên không gửi phần mô tả dài hơn mức này xuống trình duyệt (API vẫn trả mô tả đầy đủ).
    /// </summary>
    public string DescriptionPreview => MenuText.Preview(ShortDescription, MenuText.DescriptionPreviewLength);
}

public static class MenuText
{
    /// <summary>Khoảng 3 dòng chữ trên màn hình rộng 360px.</summary>
    public const int DescriptionPreviewLength = 140;

    /// <summary>Cắt chuỗi tại ranh giới từ gần nhất và thêm dấu "…" khi dài hơn <paramref name="maxLength"/>.</summary>
    public static string Preview(string? text, int maxLength)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length <= maxLength) return value;
        var cut = value.LastIndexOf(' ', maxLength);
        if (cut < maxLength / 2) cut = maxLength;
        return value[..cut].TrimEnd(' ', ',', ';', ':', '.') + "…";
    }
}

/// <summary>S2-01 Task 3: chữ hiển thị cho món hết trong ngày.</summary>
public static class SoldOutDisplay
{
    public const string Label = "Tạm hết";
    public const string ScreenReaderNote = "Món này hiện không phục vụ trong hôm nay.";
}

/// <summary>Một nhóm món trên thực đơn công khai, giữ đúng thứ tự hiển thị.</summary>
public sealed record PublicMenuCategory(int Id, string Name, IReadOnlyList<PublicMenuDish> Dishes);

/// <summary>Dữ liệu thô của một món đang bán, dùng chung cho store SQL và store bộ nhớ.</summary>
public sealed record OnSaleDishRow(
    int Id,
    int CategoryId,
    string Name,
    string? ShortDescription,
    int PriceVnd,
    string Unit,
    string? ImagePath,
    int SortOrder,
    bool SoldOutToday);

public static class MoneyFormat
{
    // Không phụ thuộc dữ liệu văn hoá của máy chủ (Windows/Linux/ICU): luôn dùng dấu chấm phân tách hàng nghìn.
    private static readonly NumberFormatInfo VietnameseNumbers = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3]
    };

    public const string VndSymbol = "₫";

    public static string Vnd(long amount) => amount.ToString("#,##0", VietnameseNumbers) + " " + VndSymbol;
}

public static class DishImage
{
    public const string DefaultImage = "/images/thuc-don/mac-dinh.svg";

    /// <summary>
    /// Ảnh mặc định theo nhóm món. So khớp theo từ khoá trong tên nhóm (không phân biệt hoa thường và dấu)
    /// để các nhóm như "Giải khát", "Nước ngọt", "Bia", "Cà phê" vẫn dùng đúng ảnh đồ uống.
    /// Nhóm không khớp từ khoá nào trả về null để dùng ảnh mặc định chung.
    /// </summary>
    public static string? DefaultForCategory(string? categoryName)
    {
        var name = $" {RemoveDiacritics(categoryName)} ";
        if (name.Trim().Length == 0) return null;

        foreach (var (keywords, image) in CategoryImageKeywords)
        {
            if (keywords.Any(k => name.Contains($" {k} ", StringComparison.Ordinal) || (k.Contains(' ') && name.Contains(k, StringComparison.Ordinal))))
                return image;
        }
        return null;
    }

    // Thứ tự quan trọng: nhóm đồ uống được xét trước để "Nước giải khát" không rơi vào nhóm khác.
    private static readonly (string[] Keywords, string Image)[] CategoryImageKeywords =
    {
        (new[] { "do uong", "thuc uong", "nuoc uong", "giai khat", "nuoc ngot", "nuoc ep", "nuoc trai cay",
                 "sinh to", "ca phe", "cafe", "coffee", "tra sua", "tra", "bia", "ruou", "soda", "drink", "drinks", "beverage", "beverages" },
            "/images/thuc-don/do-uong.svg"),
        (new[] { "lau", "hotpot" }, "/images/thuc-don/lau.svg"),
        (new[] { "trang mieng", "che", "kem", "banh ngot", "dessert", "desserts" }, "/images/thuc-don/trang-mieng.svg"),
        (new[] { "khai vi", "goi", "nom", "appetizer", "appetizers", "starter", "starters" }, "/images/thuc-don/khai-vi.svg"),
        (new[] { "mon chinh", "main", "main course" }, "/images/thuc-don/mon-chinh.svg"),
    };

    private static string RemoveDiacritics(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant()
            .Replace('đ', 'd')
            .Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        return System.Text.RegularExpressions.Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>
    /// Chọn ảnh hiển thị: ảnh món, ảnh mặc định của nhóm, cuối cùng là ảnh mặc định chung.
    /// Chỉ chấp nhận đường dẫn nội bộ ("/...") hoặc https để tránh URL lạ trong thẻ img.
    /// </summary>
    public static string Resolve(string? path, string? categoryDefault = null)
    {
        return ResolveAllowed(path) ?? ResolveAllowed(categoryDefault) ?? DefaultImage;
    }

    private static string? ResolveAllowed(string? path)
    {
        var value = path?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\')) return value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return uri.ToString();
        return null;
    }
}

public static class PublicMenuBuilder
{
    /// <summary>
    /// Gom món đang bán vào các nhóm đang sử dụng. Nhóm giữ thứ tự đầu vào; món xếp theo thứ tự hiển thị rồi Id.
    /// Nhóm đang sử dụng nhưng chưa có món vẫn được trả về để khách thấy danh mục đầy đủ.
    /// </summary>
    public static IReadOnlyList<PublicMenuCategory> Build(
        IEnumerable<(int Id, string Name, string? DefaultImagePath)> activeCategories,
        IEnumerable<OnSaleDishRow> onSaleDishes)
    {
        var byCategory = onSaleDishes.ToLookup(m => m.CategoryId);
        return activeCategories
            .Select(n => new PublicMenuCategory(n.Id, n.Name, byCategory[n.Id]
                .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
                .Select(m => new PublicMenuDish(
                    m.Id,
                    m.Name,
                    m.ShortDescription?.Trim() ?? string.Empty,
                    m.PriceVnd,
                    m.Unit,
                    DishImage.Resolve(m.ImagePath, n.DefaultImagePath ?? DishImage.DefaultForCategory(n.Name)),
                    m.SoldOutToday))
                .ToArray()))
            .ToArray();
    }
}
