using RestaurantManagement.Web.Models.Orders;

namespace RestaurantManagement.Web.Services.Pdf;

/// <summary>
/// S4-01 Task 4: tạo PDF hoá đơn tạm tính. Font nạp một lần khi cần lần đầu (từ thư mục Fonts của web);
/// nạp hỏng (thiếu tệp font…) thì lần sau thử lại, controller báo lỗi thân thiện thay vì trang lỗi chung.
/// </summary>
public sealed class BillPdfService(string fontDirectory)
{
    public const string FailureMessage =
        "Không tạo được tệp PDF. Vui lòng thử lại; nếu vẫn lỗi, bấm “In” rồi chọn máy in “Lưu dưới dạng PDF”.";

    private readonly Lazy<BillPdfBuilder> _builder =
        new(() => BillPdfBuilder.FromDirectory(fontDirectory), LazyThreadSafetyMode.PublicationOnly);

    public string FontDirectory { get; } = fontDirectory;

    public BillPdf Build(TableBillPreview preview, TableBillHeader header) => _builder.Value.Build(preview, header);

    /// <summary>Thư mục Fonts: cạnh mã nguồn (dotnet run) hoặc cạnh bản build / publish.</summary>
    public static string FindFontDirectory(string contentRoot)
    {
        foreach (var directory in new[] { Path.Combine(contentRoot, "Fonts"), Path.Combine(AppContext.BaseDirectory, "Fonts") })
            if (File.Exists(Path.Combine(directory, BillPdfBuilder.RegularFontFile))) return directory;
        return Path.Combine(contentRoot, "Fonts");
    }
}
