# S2-09 Task 2: Tự động gửi lại email khi lần gửi đầu thất bại

Khi email đặt bàn gửi lỗi, hệ thống tự gửi lại **tối đa 3 lần**, mỗi lần **cách nhau 5 phút**, và ghi lại **kết quả cuối cùng**. Nhân viên xem được số lần đã thử, kết quả của từng lần và kết quả cuối cùng. Khách thấy trạng thái email trên trang xác nhận đặt bàn.

**AC hoàn tất:** email thất bại được thử lại tối đa 3 lần cách nhau 5 phút và kết quả cuối cùng được ghi lại. Story S2-09 hoàn tất.

## Quy tắc

| | |
| --- | --- |
| Số lần thử tối đa | 4 = **1 lần gửi đầu + 3 lần gửi lại** (khớp ràng buộc `AttemptCount BETWEEN 0 AND 4` có từ đầu) |
| Khoảng cách | Lần gửi lại tiếp theo đúng **5 phút** sau khi lần thử trước kết thúc (`NextAttemptAt = CompletedAt + 5 phút`) |
| Kết quả cuối cùng | **Đã gửi**: thành công ở bất kỳ lần thử nào. **Thất bại**: lần gửi lại thứ 3 vẫn lỗi. Sau đó không gửi nữa. |
| Loại email được gửi lại | Email xác nhận đặt bàn (`BookingReceived`) và email báo huỷ (`BookingCancelled`) |
| Không gửi lại | Email xác nhận của lượt đã huỷ, bị từ chối hoặc khách không đến (chuyển sang *Đã huỷ gửi*) |
| Lần thử bị gián đoạn | Web dừng giữa lúc gửi (khoá quá 2 phút): lần đó được ghi *Thất bại*, rồi tính như lỗi bình thường |

Quy tắc nằm trong database (`usp_CompleteEmail`, `usp_ClaimDueBookingEmail`), nên chạy nhiều web cùng lúc cũng không gửi trùng. `EmailRetryPolicy` trong code mô tả cùng quy tắc để hiển thị và kiểm thử.

## Cách hoạt động

1. **Lần gửi đầu** vẫn như Task 1: gửi ngay sau khi đặt bàn. Nếu lỗi, `usp_CompleteEmail` giữ email ở *Chờ gửi lại* và hẹn `NextAttemptAt` sau 5 phút.
2. **`BookingEmailRetryWorker`** chạy nền trong web, cứ 30 giây (`Email:RetryPollSeconds`) gọi `usp_ClaimDueBookingEmail`. Thủ tục này chỉ nhận email đã thử ít nhất 1 lần, còn lần thử và đã tới giờ gửi lại. Worker gửi lại rồi lưu kết quả qua `usp_CompleteEmail`.
3. **Mỗi lần thử** được ghi một dòng trong bảng mới `dbo.EmailAttempts`: số thứ tự, giờ bắt đầu, giờ kết thúc, *Đang gửi / Thành công / Thất bại* và lỗi của riêng lần đó.
4. **Lượt đặt bàn được cập nhật sau mỗi lần thử**: các cột mới `Reservations.ConfirmationEmailStatus` (*Pending, Sending, Retrying, Sent, Failed, Cancelled*), `ConfirmationEmailAttempts` và `ConfirmationEmailUpdatedAt`.

## Màn hình

**Nhân viên, ở Chi tiết đặt bàn:**
- Dòng *Trạng thái email xác nhận*, ví dụ “Email: chưa gửi được, sẽ tự gửi lại (đã thử 2/4)”.
- Khu vực *Email xác nhận* có:
  - số lần thử, ví dụ `4/4 (3 lần gửi lại, tối đa 3 lần gửi lại, cách nhau 5 phút)`;
  - giờ thử lại kế tiếp;
  - **kết quả cuối cùng**, ví dụ *Thành công lúc … (ở lần thử thứ 2)* hoặc *Thất bại sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)*;
  - **bảng từng lần thử**: Lần gửi đầu, Lần gửi lại 1–3, kèm thời gian, kết quả và lỗi.

  Khu vực này tự làm mới 15 giây một lần cho tới khi có kết quả cuối cùng.

**Nhân viên, ở Danh sách đặt bàn:** dòng nhỏ dưới trạng thái, ví dụ “Email: gửi thất bại sau 4 lần thử”.

**Khách, ở trang xác nhận đặt bàn:**
- Khi email chưa gửi được, khách thấy: *“Email xác nhận chưa gửi được. Lượt đặt bàn của bạn vẫn được ghi nhận — vui lòng lưu lại mã đặt bàn A05. Hệ thống sẽ tự gửi lại lúc 19:05 (đã thử 1/4 lần; tối đa 3 lần gửi lại, mỗi lần cách nhau 5 phút).”*
- Trang tự cập nhật tới khi có trạng thái cuối cùng:
  - *“Email xác nhận đã được gửi tới k\*\*\*h@… Lần gửi đầu chưa thành công; hệ thống đã tự gửi lại và gửi được ở lần thử thứ 2.”*
  - hoặc *“Không gửi được email xác nhận tới … sau 4 lần thử (lần gửi đầu và 3 lần gửi lại). Lượt đặt bàn của bạn vẫn được ghi nhận — vui lòng lưu lại mã đặt bàn A05.”*
- Trang chỉ đọc lượt đặt bàn vừa tạo trong chính trình duyệt đó. Id lấy từ TempData, không nhận Id từ URL.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `030_EmailRetry.sql` (mới): bảng `EmailAttempts`, 3 cột trạng thái email trên `Reservations`, `usp_ClaimDueBookingEmail` (mới); sửa `usp_ClaimEmail`, `usp_ClaimReservationEmail`, `usp_CompleteEmail` |
| Service | `EmailRetryPolicy.cs`, `BookingEmailRetryWorker.cs` (mới); `BookingEmailDispatcher.RetryDueAsync`; `ReservationEmailStatusStore` đọc lịch sử lần thử |
| Model | `ReservationEmailStatus.cs` (lần thử, kết quả cuối cùng), `BookingSuccessViewModel.cs` (`BookingEmailCustomerStatus`), `ReservationViewModels.cs` |
| Controller | `ReservationsController`: `Success` hiển thị trạng thái mới nhất, thêm `BookingEmailStatus` (JSON) |
| View/JS/CSS | `_EmailStatus.cshtml`, `Success.cshtml`, `Index.cshtml`, `Details.cshtml`, `booking-email-status.js` (mới), `reservation-email.css` |
| Cấu hình | `appsettings.json`: `Email:RetryPollSeconds` = 30 (0 = tắt) |
| DbTool | lệnh `email-retry-now` để demo không phải chờ 5 phút |
| Test | `EmailRetryTests.cs` (mới), `ReservationEmailStatusTests.cs`; `BookingConfirmationVerification.cs` (SQL Server thật + web thật) |

## Demo: cố tình làm email gửi lỗi

```powershell
cd D:\HeThongDatBan-GoiMon-Chinh
$env:RM_CONNECTION_STRING = '<chuỗi kết nối>'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate      # phải thấy Applied: 030_EmailRetry.sql
```

1. Làm cho việc gửi email luôn lỗi bằng cách trỏ SMTP tới cổng không có máy chủ, rồi chạy web:
   ```powershell
   $env:Email__Host = '127.0.0.1'; $env:Email__Port = '2599'; $env:Email__EnableSsl = 'false'; $env:Email__FromAddress = 'no-reply@example.com'
   dotnet run --project src/RestaurantManagement.Web
   ```
2. Đặt bàn có nhập email. Trang xác nhận hiện “Email xác nhận chưa gửi được… Hệ thống sẽ tự gửi lại lúc …”.
3. Mở chi tiết lượt đặt bàn: *Đang thử gửi lại*, `1/4`, bảng lần thử có *Lần gửi đầu – Thất bại* cùng lỗi.
4. Chờ 5 phút, hoặc mở PowerShell thứ hai và tua nhanh:
   ```powershell
   dotnet run --project tools/RestaurantManagement.DbTool -- email-retry-now
   ```
   Trong vòng 30 giây, web gửi lại và bảng có thêm *Lần gửi lại 1*. Lặp lại thêm 2 lần: kết quả cuối cùng *Thất bại sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)*. Chạy `email-retry-now` thêm lần nữa thì không có lần thử thứ 5.
5. Thử trường hợp **thành công ở lần thử thứ 2**:
   - đặt bàn mới khi SMTP đang lỗi;
   - dừng web, xoá các biến `$env:Email__*` (`Remove-Item Env:Email__Host` …) để email được ghi vào `App_Data/emails`, rồi chạy lại web;
   - chạy `email-retry-now`.
   
   Chi tiết hiện *Thành công lúc … (ở lần thử thứ 2)*.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

`EmailRetryTests` chạy không cần database, dùng hàng đợi và đồng hồ giả. Bài test kiểm tra các trường hợp sau:
- gửi lại sau khi lần đầu lỗi;
- không gửi trước 5 phút, và các lần thử cách nhau đúng 5 phút;
- không thử quá 3 lần gửi lại;
- thành công ở lần thử thứ 2;
- cả 3 lần gửi lại đều lỗi thì có kết quả cuối cùng;
- nhân viên và khách thấy đúng số lần thử và kết quả.

Lệnh `verify` kiểm tra lại các trường hợp trên với SQL Server và web thật, gồm cả việc database hẹn đúng 300 giây giữa các lần thử.
