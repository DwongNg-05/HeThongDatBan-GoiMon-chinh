namespace RestaurantManagement.Web.Services.EmailVerification;

/// <summary>
/// Thương hiệu dùng chung cho mọi email gửi khách / nhân viên: tên quán "Bếp Nhà" và logo ở đầu email.
/// Logo được đính kèm vào email dạng ảnh nhúng (Content-ID "bepnha-logo"), nên hiện được trong Gmail/Outlook
/// mà không cần máy chủ web có địa chỉ công khai. Không tìm thấy tệp logo thì email vẫn gửi bình thường, chỉ không có logo.
/// </summary>
public static class EmailBranding
{
    public const string RestaurantName = "Bếp Nhà";
    public const string LogoContentId = "bepnha-logo";
    /// <summary>Logo chữ trắng nền trong suốt (đặt trên dải đỏ ở đầu email), đường dẫn tính từ wwwroot.</summary>
    public const string LogoRelativePath = "images/brand/bep-nha-logo-email.png";

    /// <summary>Thẻ ảnh logo (thay cho chữ "Bếp Nhà") ở đầu email, trên dải màu đỏ thương hiệu.</summary>
    public const string LogoImg =
        "<img src=\"cid:" + LogoContentId + "\" alt=\"Bếp Nhà\" width=\"88\" height=\"82\" " +
        "style=\"display:block;width:88px;height:auto;border:0;outline:none\" />";

    /// <summary>Chữ thay thế khi không đính kèm được logo.</summary>
    private const string LogoFallback =
        "<span style=\"display:block;color:#ffffff;font-size:24px;font-weight:700;white-space:nowrap\">Bếp Nhà</span>";

    /// <summary>
    /// Dòng đầu email: logo Bếp Nhà bên trái, gạch dọc mảnh, rồi tiêu đề (ví dụ "Xác nhận đặt bàn").
    /// <paramref name="encodedTitle"/> phải được mã hoá HTML trước khi truyền vào.
    /// </summary>
    public static string Header(string encodedTitle, string background) =>
        $"<tr><td style=\"background:{background};padding:16px 24px\">" +
        "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\"><tr>" +
        $"<td style=\"padding:0 16px 0 0;vertical-align:middle\">{LogoImg}</td>" +
        "<td style=\"padding:6px 0 6px 16px;vertical-align:middle;border-left:1px solid rgba(255,255,255,.5);" +
        $"color:#ffffff;font-size:20px;font-weight:700;line-height:1.3\">{encodedTitle}</td>" +
        "</tr></table></td></tr>";

    /// <summary>
    /// Tên quán hiển thị trong email: dữ liệu mẫu cũ ghi "Nhà hàng Demo" (migration 005) được đổi thành "Bếp Nhà";
    /// tên trống cũng dùng "Bếp Nhà".
    /// </summary>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            || trimmed.Equals("Nhà hàng Demo", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Nhà hàng", StringComparison.OrdinalIgnoreCase)
            ? RestaurantName
            : trimmed;
    }

    /// <summary>Đường dẫn tệp logo trên máy chủ, hoặc null nếu không có.</summary>
    public static string? FindLogo(IWebHostEnvironment environment)
    {
        try
        {
            var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
                ? Path.Combine(environment.ContentRootPath, "wwwroot")
                : environment.WebRootPath;
            var path = Path.Combine(webRoot, LogoRelativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Không đính kèm được ảnh: thay logo bằng chữ "Bếp Nhà" (tránh ảnh vỡ trong email).</summary>
    public static string RemoveLogo(string html) => html.Replace(LogoImg, LogoFallback);

    /// <summary>Email lưu thành tệp .html (chưa cấu hình SMTP): thay ảnh nhúng bằng ảnh base64 để mở trong trình duyệt vẫn thấy logo.</summary>
    public static string InlineLogoForFile(string html, IWebHostEnvironment environment)
    {
        if (!html.Contains(LogoImg)) return html;
        var logo = FindLogo(environment);
        if (logo is null) return RemoveLogo(html);
        var dataUri = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(logo));
        return html.Replace("cid:" + LogoContentId, dataUri);
    }
}
