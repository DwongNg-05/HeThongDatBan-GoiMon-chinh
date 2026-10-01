using System.Globalization;

namespace RestaurantManagement.Web.Services;

/// <summary>Một món trên thực đơn công khai. Chỉ chứa dữ liệu khách được xem.</summary>
public sealed record MonThucDonCongKhai(
    int Id,
    string Ten,
    string MoTaNgan,
    int GiaVnd,
    string DonViTinh,
    string AnhUrl,
    bool HetTrongNgay)
{
    /// <summary>Giá hiển thị theo chuẩn Việt Nam, ví dụ "45.000 ₫".</summary>
    public string GiaHienThi => DinhDangTien.Vnd(GiaVnd);
}

/// <summary>Một nhóm món trên thực đơn công khai, giữ đúng thứ tự hiển thị.</summary>
public sealed record NhomThucDonCongKhai(int Id, string Ten, IReadOnlyList<MonThucDonCongKhai> MonAn);

/// <summary>Dữ liệu thô của một món đang bán, dùng chung cho store SQL và store bộ nhớ.</summary>
public sealed record MonDangBanTho(
    int Id,
    int NhomMonId,
    string Ten,
    string? MoTaNgan,
    int GiaVnd,
    string DonViTinh,
    string? DuongDanAnh,
    int ThuTuHienThi,
    bool HetTrongNgay);

public static class DinhDangTien
{
    // Không phụ thuộc dữ liệu văn hoá của máy chủ (Windows/Linux/ICU): luôn dùng dấu chấm phân tách hàng nghìn.
    private static readonly NumberFormatInfo SoViet = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3]
    };

    public const string KyHieuVnd = "₫";

    public static string Vnd(long soTien) => soTien.ToString("#,##0", SoViet) + " " + KyHieuVnd;
}

public static class AnhMonAn
{
    public const string AnhMacDinh = "/images/thuc-don/mac-dinh.svg";

    /// <summary>
    /// Chọn ảnh hiển thị: ảnh món, nếu không có thì ảnh mặc định chung.
    /// Chỉ chấp nhận đường dẫn nội bộ ("/...") hoặc https để tránh URL lạ trong thẻ img.
    /// </summary>
    public static string ChonAnh(string? duongDan)
    {
        var value = duongDan?.Trim();
        if (string.IsNullOrEmpty(value)) return AnhMacDinh;
        if (value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\')) return value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return uri.ToString();
        return AnhMacDinh;
    }
}

public static class ThucDonCongKhaiBuilder
{
    /// <summary>
    /// Gom món đang bán vào các nhóm đang sử dụng. Nhóm giữ thứ tự đầu vào; món xếp theo thứ tự hiển thị rồi Id.
    /// Nhóm đang sử dụng nhưng chưa có món vẫn được trả về để khách thấy danh mục đầy đủ.
    /// </summary>
    public static IReadOnlyList<NhomThucDonCongKhai> Tao(
        IEnumerable<(int Id, string Ten)> nhomDangSuDung,
        IEnumerable<MonDangBanTho> monDangBan)
    {
        var theoNhom = monDangBan.ToLookup(m => m.NhomMonId);
        return nhomDangSuDung
            .Select(n => new NhomThucDonCongKhai(n.Id, n.Ten, theoNhom[n.Id]
                .OrderBy(m => m.ThuTuHienThi).ThenBy(m => m.Id)
                .Select(m => new MonThucDonCongKhai(
                    m.Id,
                    m.Ten,
                    m.MoTaNgan?.Trim() ?? string.Empty,
                    m.GiaVnd,
                    m.DonViTinh,
                    AnhMonAn.ChonAnh(m.DuongDanAnh),
                    m.HetTrongNgay))
                .ToArray()))
            .ToArray();
    }
}
