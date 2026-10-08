using System.ComponentModel.DataAnnotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace RestaurantManagement.Web.Services;

/// <summary>Kết quả kiểm tra tệp ảnh món: lỗi (nếu có) và đuôi tệp chuẩn hoá (.jpg hoặc .png).</summary>
public sealed record ImageCheckResult(string? Error, string? Extension)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Quy tắc ảnh món ăn: chỉ JPG/PNG, tối đa 5 MB (khớp CHECK ImageSizeBytes của bảng MenuItems).
/// Kiểm tra cả đuôi tệp lẫn chữ ký nội dung, không tin Content-Type do trình duyệt gửi.
/// </summary>
public static class DishImageRules
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 1024;
    public const string WebFolder = "/uploads/mon-an/";
    public const string AcceptHtml = ".jpg,.jpeg,.png,image/jpeg,image/png";

    public const string EmptyFileError = "Tệp ảnh rỗng. Vui lòng chọn ảnh khác.";
    public const string TooLargeError = "Ảnh không được lớn hơn 5 MB.";
    public const string FormatError = "Chỉ chấp nhận ảnh định dạng JPG hoặc PNG.";
    public const string ContentError = "Nội dung tệp không phải ảnh JPG/PNG hợp lệ.";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    public static ImageCheckResult Validate(IFormFile file)
    {
        if (file.Length <= 0) return new(EmptyFileError, null);
        if (file.Length > MaxBytes) return new(TooLargeError, null);

        var extension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        var nameIsJpeg = extension is ".jpg" or ".jpeg";
        var nameIsPng = extension is ".png";
        if (!nameIsJpeg && !nameIsPng) return new(FormatError, null);

        var header = new byte[8];
        int bytesRead;
        using (var stream = file.OpenReadStream())
        {
            bytesRead = 0;
            int n;
            while (bytesRead < header.Length && (n = stream.Read(header, bytesRead, header.Length - bytesRead)) > 0) bytesRead += n;
        }
        var isPng = bytesRead >= PngSignature.Length && header.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature);
        var isJpeg = bytesRead >= JpegSignature.Length && header.AsSpan(0, JpegSignature.Length).SequenceEqual(JpegSignature);

        if (nameIsJpeg && isJpeg) return new(null, ".jpg");
        if (nameIsPng && isPng) return new(null, ".png");
        return new(ContentError, null);
    }
}

/// <summary>Lưu ảnh món và trả về đường dẫn web để ghi vào cột MenuItems.ImagePath.</summary>
public interface IDishImageStorage
{
    /// <summary>Lưu tệp đã hợp lệ, trả về đường dẫn dạng "/uploads/mon-an/{tên}.jpg". Ném ValidationException nếu tệp không hợp lệ.</summary>
    Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>Xoá ảnh đã tải lên trước đó (chỉ trong thư mục ảnh món; bỏ qua ảnh mẫu và đường dẫn lạ).</summary>
    void Delete(string? path);
}

/// <summary>Lưu ảnh đã thu nhỏ và nén vào wwwroot/uploads/mon-an; không dùng tên tệp người dùng gửi.</summary>
public sealed class DiskDishImageStorage : IDishImageStorage
{
    private readonly string _folder;

    public DiskDishImageStorage(string folder)
    {
        _folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(_folder);
    }

    public string Folder => _folder;

    public async Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var result = DishImageRules.Validate(file);
        if (!result.IsValid) throw new ValidationException(result.Error);

        var fileName = $"{Guid.NewGuid():N}{result.Extension}";
        var filePath = Path.Combine(_folder, fileName);
        try
        {
            await using var target = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var source = file.OpenReadStream();
            using var image = await Image.LoadAsync(source, cancellationToken);

            image.Mutate(operation => operation.AutoOrient());
            var scale = Math.Min(1d, (double)DishImageRules.MaxDimension / Math.Max(image.Width, image.Height));
            if (scale < 1d)
            {
                var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                var height = Math.Max(1, (int)Math.Round(image.Height * scale));
                image.Mutate(operation => operation.Resize(width, height, KnownResamplers.Lanczos3));
            }

            if (result.Extension == ".jpg")
                await image.SaveAsJpegAsync(target, new JpegEncoder { Quality = 82, SkipMetadata = true }, cancellationToken);
            else
                await image.SaveAsPngAsync(target, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression, SkipMetadata = true }, cancellationToken);

            if (Math.Max(image.Width, image.Height) > DishImageRules.MaxDimension)
                throw new InvalidDataException("Ảnh sau khi tối ưu vẫn vượt quá kích thước cho phép.");
        }
        catch (UnknownImageFormatException ex)
        {
            TryDelete(filePath);
            throw new ValidationException(DishImageRules.ContentError, ex);
        }
        catch (InvalidImageContentException ex)
        {
            TryDelete(filePath);
            throw new ValidationException(DishImageRules.ContentError, ex);
        }
        catch
        {
            TryDelete(filePath);
            throw;
        }
        return DishImageRules.WebFolder + fileName;
    }

    public void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(DishImageRules.WebFolder, StringComparison.Ordinal)) return;
        var fileName = path[DishImageRules.WebFolder.Length..];
        // Chỉ nhận đúng một tên tệp, không có thư mục con hay "..".
        if (fileName.Length == 0 || fileName != Path.GetFileName(fileName) || fileName.Contains("..")) return;
        File.Delete(Path.Combine(_folder, fileName));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
