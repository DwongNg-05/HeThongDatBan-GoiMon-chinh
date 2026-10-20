namespace RestaurantManagement.Web.Services.EmailVerification;

/// <summary>
/// Thương hiệu dùng chung cho mọi email gửi khách / nhân viên: tên quán "Bếp Nhà", logo và ảnh của thiệp mời.
/// Ảnh được đính kèm vào email dạng ảnh nhúng (Content-ID), nên hiện được trong Gmail/Outlook
/// mà không cần máy chủ web có địa chỉ công khai. Thiếu tệp ảnh nào thì email vẫn gửi bình thường, chỉ bỏ ảnh đó.
/// </summary>
public static class EmailBranding
{
    public const string RestaurantName = "Bếp Nhà";
    public const string LogoContentId = "bepnha-logo";
    /// <summary>Logo chữ trắng nền trong suốt (đặt trên nền đỏ / đỏ rượu vang ở đầu email), đường dẫn tính từ wwwroot.</summary>
    public const string LogoRelativePath = "images/brand/bep-nha-logo-email.png";

    /// <summary>Ảnh đầu thiệp mời: nền đỏ rượu vang ở trên, giấy kem ở dưới, con dấu sáp đỏ ở giữa (email đặt bàn / huỷ đặt bàn).</summary>
    public const string InviteTopContentId = "bepnha-thiep-dau";
    public const string InviteTopRelativePath = "images/email/thiep-moi-dau.jpg";
    /// <summary>Vân giấy kem của thiệp mời (lặp làm nền thân thiệp).</summary>
    public const string PaperContentId = "bepnha-giay";
    public const string PaperRelativePath = "images/email/giay-thiep.jpg";

    /// <summary>Một ảnh nhúng trong email: Content-ID, đường dẫn tính từ wwwroot, kiểu tệp.</summary>
    public sealed record InlineImage(string ContentId, string RelativePath, string MediaType);

    /// <summary>Mọi ảnh có thể nhúng trong email; chỉ ảnh nào được nhắc tới trong nội dung (cid:...) mới được đính kèm.</summary>
    public static readonly IReadOnlyList<InlineImage> Images =
    [
        new(LogoContentId, LogoRelativePath, "image/png"),
        new(InviteTopContentId, InviteTopRelativePath, "image/jpeg"),
        new(PaperContentId, PaperRelativePath, "image/jpeg")
    ];

    /// <summary>Thẻ ảnh logo (thay cho chữ "Bếp Nhà") ở đầu email.</summary>
    public const string LogoImg =
        "<img src=\"cid:" + LogoContentId + "\" alt=\"Bếp Nhà\" width=\"88\" height=\"82\" " +
        "style=\"display:block;width:88px;height:auto;border:0;outline:none\" />";

    /// <summary>Thẻ ảnh đầu thiệp mời (con dấu sáp nằm giữa mép nền đỏ và mép giấy).</summary>
    public const string InviteTopImg =
        "<img src=\"cid:" + InviteTopContentId + "\" alt=\"\" width=\"520\" height=\"118\" " +
        "style=\"display:block;width:100%;max-width:520px;height:auto;border:0;outline:none\" />";

    /// <summary>Nền vân giấy cho ô thân thiệp: thuộc tính background (Outlook cũ) và CSS background-image.</summary>
    public const string PaperBackgroundAttr = " background=\"cid:" + PaperContentId + "\"";
    public const string PaperBackgroundCss = "background-image:url(cid:" + PaperContentId + ");background-repeat:repeat;";

    /// <summary>Chữ thay thế khi không đính kèm được logo.</summary>
    private const string LogoFallback =
        "<span style=\"display:block;color:#ffffff;font-size:24px;font-weight:700;white-space:nowrap\">Bếp Nhà</span>";

    /// <summary>Thiếu ảnh đầu thiệp: chỉ để lại một khoảng trống nhỏ màu giấy.</summary>
    private const string InviteTopFallback =
        "<div style=\"height:28px;line-height:28px;font-size:0;background:#f5ede4\">&nbsp;</div>";

    /// <summary>
    /// Dòng đầu email: logo Bếp Nhà bên trái, gạch dọc mảnh, rồi tiêu đề (ví dụ "Nhắc lịch đặt bàn").
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

    /// <summary>Đường dẫn tệp (tính từ wwwroot) trên máy chủ, hoặc null nếu không có.</summary>
    public static string? FindFile(IWebHostEnvironment environment, string relativePath)
    {
        try
        {
            var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
                ? Path.Combine(environment.ContentRootPath, "wwwroot")
                : environment.WebRootPath;
            var path = Path.Combine(webRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Đường dẫn tệp logo trên máy chủ, hoặc null nếu không có.</summary>
    public static string? FindLogo(IWebHostEnvironment environment) => FindFile(environment, LogoRelativePath);

    /// <summary>Các ảnh nhúng mà nội dung email nhắc tới (cid:...) và có tệp trên máy chủ — để đính kèm khi gửi.</summary>
    public static IReadOnlyList<(InlineImage Image, string Path)> FindImages(string html, IWebHostEnvironment environment)
    {
        var found = new List<(InlineImage, string)>();
        foreach (var image in Images)
        {
            if (!html.Contains("cid:" + image.ContentId, StringComparison.Ordinal)) continue;
            var path = FindFile(environment, image.RelativePath);
            if (path is not null) found.Add((image, path));
        }
        return found;
    }

    /// <summary>Bỏ các ảnh không đính kèm được (tránh ảnh vỡ trong email): logo thành chữ, ảnh đầu thiệp thành khoảng trống, nền giấy thành màu giấy.</summary>
    public static string RemoveMissingImages(string html, IEnumerable<string> attachedContentIds)
    {
        var attached = attachedContentIds.ToHashSet(StringComparer.Ordinal);
        if (!attached.Contains(LogoContentId)) html = RemoveLogo(html);
        if (!attached.Contains(InviteTopContentId)) html = html.Replace(InviteTopImg, InviteTopFallback);
        if (!attached.Contains(PaperContentId)) html = html.Replace(PaperBackgroundAttr, string.Empty).Replace(PaperBackgroundCss, string.Empty);
        return html;
    }

    /// <summary>Không đính kèm được ảnh: thay logo bằng chữ "Bếp Nhà" (tránh ảnh vỡ trong email).</summary>
    public static string RemoveLogo(string html) => html.Replace(LogoImg, LogoFallback);

    /// <summary>Email lưu thành tệp .html (chưa cấu hình SMTP): thay ảnh nhúng bằng ảnh base64 để mở trong trình duyệt vẫn thấy logo và thiệp.</summary>
    public static string InlineLogoForFile(string html, IWebHostEnvironment environment)
    {
        var images = FindImages(html, environment);
        html = RemoveMissingImages(html, images.Select(i => i.Image.ContentId));
        foreach (var (image, path) in images)
        {
            var dataUri = $"data:{image.MediaType};base64," + Convert.ToBase64String(File.ReadAllBytes(path));
            html = html.Replace("cid:" + image.ContentId, dataUri);
        }
        return html;
    }
}
