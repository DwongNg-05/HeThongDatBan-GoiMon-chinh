using System.Globalization;
using System.Text;

namespace RestaurantManagement.Web.Models.Orders;

/// <summary>
/// S4-01 Task 4: quy tắc đặt tên tệp PDF hoá đơn tạm tính (đề xuất, cần PO chốt — docs/S4-01-Task4.md):
/// <c>TamTinh_{mã bàn}_{yyyyMMdd-HHmm}.pdf</c> theo giờ in (giờ Việt Nam), ví dụ <c>TamTinh_A03_20261015-2230.pdf</c>;
/// nhóm bàn đã gộp nối bằng "-": <c>TamTinh_A03-A04_20261015-2230.pdf</c>.
/// Chỉ dùng chữ không dấu, chữ số, "-" và "_" để tên tệp an toàn trên mọi máy / ứng dụng gửi tệp; ký tự khác thành "-".
/// </summary>
public static class BillPdfFileName
{
    public const string Prefix = "TamTinh";
    public const string Pattern = "TamTinh_{mã bàn}_{yyyyMMdd-HHmm}.pdf";

    public static string For(TableBillHeader header) => For(header.Tables, header.PrintedAt);

    /// <param name="tables">Mã bàn hiện tại, ví dụ "A03" hoặc "A03, A04".</param>
    /// <param name="printedAtVietnam">Thời điểm in theo giờ Việt Nam.</param>
    public static string For(string tables, DateTime printedAtVietnam) =>
        $"{Prefix}_{TableSegment(tables)}_{printedAtVietnam.ToString("yyyyMMdd'-'HHmm", CultureInfo.InvariantCulture)}.pdf";

    private static string TableSegment(string tables)
    {
        var plain = new StringBuilder();
        foreach (var c in tables.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var ch = c switch { 'đ' => 'd', 'Đ' => 'D', _ => c };
            plain.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        }
        var segment = string.Join('-', plain.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return segment.Length == 0 ? "Ban" : segment;
    }
}
