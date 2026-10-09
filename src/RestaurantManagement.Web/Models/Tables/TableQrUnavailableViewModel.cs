using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Models.Tables;

/// <summary>S3-01 Task 1: thông báo cho khách khi mã QR hợp lệ nhưng bàn chưa mở được phiên gọi món.</summary>
public sealed record TableQrUnavailableViewModel(QrStartOutcome Outcome, string Title, string Message, int StatusCode, bool CanRetry)
{
    /// <summary>Mã QR công khai để thử lại (chỉ khi <see cref="CanRetry"/>).</summary>
    public string Token { get; init; } = string.Empty;

    public static TableQrUnavailableViewModel For(QrStartOutcome outcome) => outcome switch
    {
        QrStartOutcome.TableReserved => new(outcome, "Bàn đã được đặt trước",
            "Bàn này đang giữ cho khách đã đặt. Vui lòng gọi nhân viên để được nhận bàn.", StatusCodes.Status409Conflict, false),
        QrStartOutcome.TableBusy => new(outcome, "Bàn đang được phục vụ",
            "Bàn này đang có phiên gọi món. Vui lòng gọi nhân viên để được hỗ trợ.", StatusCodes.Status409Conflict, false),
        QrStartOutcome.TableCleaning => new(outcome, "Bàn đang được dọn",
            "Vui lòng chờ nhân viên dọn xong bàn hoặc gọi nhân viên để được hỗ trợ.", StatusCodes.Status409Conflict, false),
        QrStartOutcome.NoShift => new(outcome, "Nhà hàng chưa nhận gọi món",
            "Ca phục vụ chưa mở. Vui lòng gọi nhân viên để được hỗ trợ.", StatusCodes.Status409Conflict, false),
        QrStartOutcome.OutsideOpeningHours => new(outcome, "Nhà hàng đang ngoài giờ hoạt động",
            "Hiện chưa tới giờ mở cửa hoặc đã qua giờ đóng cửa hôm nay. Vui lòng gọi nhân viên để được hỗ trợ.", StatusCodes.Status409Conflict, false),
        QrStartOutcome.SystemBusy => new(outcome, "Hệ thống đang bận",
            "Vui lòng thử lại sau ít giây.", StatusCodes.Status503ServiceUnavailable, true),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Kết quả này không dùng trang thông báo bàn.")
    };
}
