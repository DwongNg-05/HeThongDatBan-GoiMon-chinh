# S2-09 (điều chỉnh): Chỉ gửi email khi nhân viên xác nhận đặt bàn

## Luồng mới

1. **Khách đặt bàn.**
   - Trang thành công hiển thị **mã đặt bàn** (6 ký tự), có nút **Sao chép mã**.
   - Trạng thái lượt đặt là **Chờ xác nhận**.
   - Lúc này **chưa gửi email**.
2. **Khách tra cứu trước khi được xác nhận** (`/ReservationLookup`): thấy **Chờ xác nhận**, kèm giải thích bước tiếp theo.
3. **Quản lý / Phục vụ xác nhận** trên màn hình *Lượt đặt* (chọn bàn bằng các ô bấm). Hệ thống gửi email ngay:
   - Tiêu đề: **`Bếp Nhà - Xác nhận đặt bàn ED095C`** (tên nhà hàng đứng trước).
   - Nội dung: mã đặt bàn, bàn đã giữ, ngày giờ, số khách, địa chỉ, và nút **Tra cứu đặt bàn**. Nút này mở `/ReservationLookup?code=ED095C` với mã đã điền sẵn, khách chỉ cần nhập số điện thoại.
   - Gửi lỗi thì hệ thống tự gửi lại sau 5 phút, tối đa 3 lần (giống S2-09 Task 2).
   - Khách không có email: nhân viên được nhắc gọi điện báo mã.
4. **Khách tra cứu sau khi được xác nhận**: thấy **Đã xác nhận** và bàn đã giữ.
5. Email báo huỷ có tiêu đề **`Bếp Nhà - Đã huỷ đặt bàn ED095C`**.

## Thay đổi so với quyết định cũ (cần PO biết)

- Trước đây "mã đặt bàn = mã bàn" (ví dụ `A05`). Nhưng trang tra cứu chỉ nhận **mã đặt bàn 6 ký tự**, nên khách nhập mã bàn sẽ không tra cứu được.
- Bây giờ mã khách nhận (trên trang thành công, trong email và khi tra cứu) luôn là mã 6 ký tự. Bàn được hiển thị trên một dòng riêng.
- Email không thể chứa nút sao chép, vì trình đọc thư chặn JavaScript. Thay vào đó:
  - mã trong email được đặt trong khung, chọn một lần là được cả mã;
  - liên kết tra cứu trong email đã điền sẵn mã.

## File thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/050_S209EmailAfterStaffConfirm.sql`:<br>• không xếp email lúc khách đặt;<br>• đổi tiêu đề mới;<br>• huỷ các email "đã ghi nhận" cũ còn chờ gửi;<br>• gửi lại tự động áp dụng cho email xác nhận. |
| Email | `Services/Reservations/BookingConfirmationEmail.cs`, `BookingConfirmationLinks.cs`, `BookingEmailDispatcher.cs`, `EmailRetryPolicy.cs`, `Services/EmailWorker.cs` |
| Controller | `ReservationConfirmationsController.cs` (gửi email khi xác nhận), `ReservationsController.cs` (không gửi khi đặt), `ReservationLookupController.cs` (`?code=`) |
| Giao diện | • `Views/ReservationConfirmations/Details.cshtml` + `wwwroot/css/reservation-confirm.css`: thiết kế lại *Lượt đặt / Chọn bàn* dạng thẻ.<br>• `Views/Reservations/Success.cshtml`, `Views/PublicReservations/Success.cshtml`, `Views/ReservationLookup/Index.cshtml`, `wwwroot/css/reservation-email.css`.<br>• `wwwroot/js/copy-code.js` (nút sao chép).<br>• `Views/Shared/_FontAwesome.cshtml`. |
| Test | • AreaTests: `BookingConfirmationEmailTests.cs`, `BookingEmailFlowTests.cs`, `EmailRetryTests.cs`.<br>• DbTool: `BookingConfirmationVerification.cs`, `BookingEmailEndToEndVerification.cs`. |

## Chạy

```
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet build
dotnet run --project tests/RestaurantManagement.AreaTests
dotnet run --project tools/RestaurantManagement.DbTool -- verify
```
