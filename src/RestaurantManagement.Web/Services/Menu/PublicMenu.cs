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
    /// Chọn ảnh hiển thị: ảnh món, nếu không có thì ảnh mặc định chung.
    /// Chỉ chấp nhận đường dẫn nội bộ ("/...") hoặc https để tránh URL lạ trong thẻ img.
    /// </summary>
    public static string Resolve(string? path)
    {
        var value = path?.Trim();
        if (string.IsNullOrEmpty(value)) return DefaultImage;
        if (value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\')) return value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return uri.ToString();
        return DefaultImage;
    }
}

public static class PublicMenuBuilder
{
    /// <summary>
    /// Gom món đang bán vào các nhóm đang sử dụng. Nhóm giữ thứ tự đầu vào; món xếp theo thứ tự hiển thị rồi Id.
    /// Nhóm đang sử dụng nhưng chưa có món vẫn được trả về để khách thấy danh mục đầy đủ.
    /// </summary>
    public static IReadOnlyList<PublicMenuCategory> Build(
        IEnumerable<(int Id, string Name)> activeCategories,
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
                    DishImage.Resolve(m.ImagePath),
                    m.SoldOutToday))
                .ToArray()))
            .ToArray();
    }
}
