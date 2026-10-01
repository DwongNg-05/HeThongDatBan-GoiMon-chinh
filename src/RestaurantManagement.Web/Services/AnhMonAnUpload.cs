using System.ComponentModel.DataAnnotations;

namespace RestaurantManagement.Web.Services;

/// <summary>Kết quả kiểm tra tệp ảnh món: lỗi (nếu có) và đuôi tệp chuẩn hoá (.jpg hoặc .png).</summary>
public sealed record KetQuaKiemTraAnh(string? Loi, string? Duoi)
{
    public bool HopLe => Loi is null;
}

/// <summary>
/// Quy tắc ảnh món ăn: chỉ JPG/PNG, tối đa 5 MB (khớp CHECK ImageSizeBytes của bảng MenuItems).
/// Kiểm tra cả đuôi tệp lẫn chữ ký nội dung, không tin Content-Type do trình duyệt gửi.
/// </summary>
public static class QuyTacAnhMonAn
{
    public const long KichThuocToiDa = 5 * 1024 * 1024;
    public const string ThuMucWeb = "/uploads/mon-an/";
    public const string AcceptHtml = ".jpg,.jpeg,.png,image/jpeg,image/png";

    public const string LoiRong = "Tệp ảnh rỗng. Vui lòng chọn ảnh khác.";
    public const string LoiQuaLon = "Ảnh không được lớn hơn 5 MB.";
    public const string LoiDinhDang = "Chỉ chấp nhận ảnh định dạng JPG hoặc PNG.";
    public const string LoiNoiDung = "Nội dung tệp không phải ảnh JPG/PNG hợp lệ.";

    private static readonly byte[] ChuKyPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] ChuKyJpeg = [0xFF, 0xD8, 0xFF];

    public static KetQuaKiemTraAnh KiemTra(IFormFile file)
    {
        if (file.Length <= 0) return new(LoiRong, null);
        if (file.Length > KichThuocToiDa) return new(LoiQuaLon, null);

        var duoi = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        var laJpegTheoTen = duoi is ".jpg" or ".jpeg";
        var laPngTheoTen = duoi is ".png";
        if (!laJpegTheoTen && !laPngTheoTen) return new(LoiDinhDang, null);

        var dau = new byte[8];
        int daDoc;
        using (var stream = file.OpenReadStream())
        {
            daDoc = 0;
            int n;
            while (daDoc < dau.Length && (n = stream.Read(dau, daDoc, dau.Length - daDoc)) > 0) daDoc += n;
        }
        var laPng = daDoc >= ChuKyPng.Length && dau.AsSpan(0, ChuKyPng.Length).SequenceEqual(ChuKyPng);
        var laJpeg = daDoc >= ChuKyJpeg.Length && dau.AsSpan(0, ChuKyJpeg.Length).SequenceEqual(ChuKyJpeg);

        if (laJpegTheoTen && laJpeg) return new(null, ".jpg");
        if (laPngTheoTen && laPng) return new(null, ".png");
        return new(LoiNoiDung, null);
    }
}

/// <summary>Lưu ảnh món và trả về đường dẫn web để ghi vào cột MenuItems.ImagePath.</summary>
public interface IKhoAnhMonAn
{
    /// <summary>Lưu tệp đã hợp lệ, trả về đường dẫn dạng "/uploads/mon-an/{tên}.jpg". Ném ValidationException nếu tệp không hợp lệ.</summary>
    Task<string> LuuAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>Xoá ảnh đã tải lên trước đó (chỉ trong thư mục ảnh món; bỏ qua ảnh mẫu và đường dẫn lạ).</summary>
    void Xoa(string? duongDan);
}

/// <summary>Lưu ảnh vào wwwroot/uploads/mon-an với tên ngẫu nhiên; không dùng tên tệp người dùng gửi.</summary>
public sealed class KhoAnhMonAnTrenDia : IKhoAnhMonAn
{
    private readonly string _thuMuc;

    public KhoAnhMonAnTrenDia(string thuMuc)
    {
        _thuMuc = Path.GetFullPath(thuMuc);
        Directory.CreateDirectory(_thuMuc);
    }

    public string ThuMuc => _thuMuc;

    public async Task<string> LuuAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var ketQua = QuyTacAnhMonAn.KiemTra(file);
        if (!ketQua.HopLe) throw new ValidationException(ketQua.Loi);

        var tenTep = $"{Guid.NewGuid():N}{ketQua.Duoi}";
        var duongDanTep = Path.Combine(_thuMuc, tenTep);
        try
        {
            await using var dich = new FileStream(duongDanTep, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(dich, cancellationToken);
        }
        catch
        {
            TryDelete(duongDanTep);
            throw;
        }
        return QuyTacAnhMonAn.ThuMucWeb + tenTep;
    }

    public void Xoa(string? duongDan)
    {
        if (string.IsNullOrEmpty(duongDan) || !duongDan.StartsWith(QuyTacAnhMonAn.ThuMucWeb, StringComparison.Ordinal)) return;
        var tenTep = duongDan[QuyTacAnhMonAn.ThuMucWeb.Length..];
        // Chỉ nhận đúng một tên tệp, không có thư mục con hay "..".
        if (tenTep.Length == 0 || tenTep != Path.GetFileName(tenTep) || tenTep.Contains("..")) return;
        TryDelete(Path.Combine(_thuMuc, tenTep));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
