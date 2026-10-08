# S2-09 Task 1: Email xác nhận đặt bàn

Ngay khi lượt đặt bàn được ghi nhận, hệ thống gửi email xác nhận cho khách. Trang xác nhận luôn hiển thị đầy đủ mã đặt bàn, kể cả khi email chưa gửi được. Nhân viên xem được trạng thái gửi email trong màn hình chi tiết đặt bàn.

**AC hoàn tất:**
- Email được gửi trong vòng 60 giây và có đầy đủ nội dung.
- Trang xác nhận hiển thị đầy đủ mã đặt bàn.
- Nhân viên xem được trạng thái gửi email.

**Chưa có:** cơ chế tự động gửi lại khi email thất bại. Email gửi lỗi được giữ ở trạng thái chờ, kèm số lần đã thử và nội dung lỗi, để task sau gửi lại.

## Nội dung email (đề xuất, cần PO chốt)

| Mục | Nguồn |
| --- | --- |
| **Mã đặt bàn** (6 ký tự, có trong cả tiêu đề email) | `Reservations.Code` |
| **Ngày giờ** theo giờ Việt Nam, ví dụ “19:00, Thứ Hai ngày 05/10/2026” | `Reservations.StartsAt` (lưu UTC) |
| **Số khách** | `Reservations.GuestCount` |
| **Địa chỉ quán** | `RestaurantSettings.Address` |
| Tên khách, khu vực (nếu có), tên quán, điện thoại quán | `Reservations`, `RestaurantSettings` |

Tiêu đề email: `Xác nhận đặt bàn <MÃ> – <Tên quán>`. Email có hai bản, HTML và chữ thường. Mọi thông tin do khách nhập đều được mã hoá trước khi đưa vào HTML.

> Trước khi demo, sửa địa chỉ quán trong `dbo.RestaurantSettings`, vì dữ liệu mẫu đang là “Địa chỉ mẫu — thay trước khi triển khai”:
> `UPDATE dbo.RestaurantSettings SET Name=N'Bếp Nhà', Address=N'…', Phone='…' WHERE Id=1;`

## Cách hoạt động

1. `usp_CreateReservation` ghi lượt đặt bàn. Trong cùng transaction, thủ tục gọi `usp_QueueBookingEmail` để thêm một dòng `BookingReceived` vào `dbo.EmailOutbox`. Migration **026** sửa `usp_QueueBookingEmail` để lưu sẵn mã đặt bàn, ngày giờ, số khách, khu vực, cùng tên, địa chỉ và điện thoại quán vào `PayloadJson`, theo thông tin tại thời điểm đặt bàn. Tiêu đề email chứa mã đặt bàn.
2. Controller đọc `ReservationId` và `Code` do thủ tục trả về. Lúc này lượt đặt bàn **đã được lưu**.
3. `BookingEmailDispatcher.SendBookingReceivedAsync` thực hiện ba việc:
   - gọi `usp_ClaimReservationEmail` (thủ tục mới) để nhận đúng email của lượt vừa tạo, tăng số lần thử và ghi `LastAttemptAt`;
   - dựng nội dung bằng `BookingConfirmationEmail` rồi gửi qua `IEmailSender`. Thời gian chờ tối đa là 30 giây và không phụ thuộc vào yêu cầu HTTP;
   - gọi `usp_CompleteEmail` để lưu kết quả: `Sent`, hoặc `Pending` kèm `LastError` nếu gửi lỗi.
   
   Mọi lỗi phát sinh khi gửi đều được bắt lại, nên **không bao giờ làm mất lượt đặt bàn**.
4. Trang `/Reservations/Success` hiển thị:
   - mã đặt bàn cỡ chữ lớn, ngày giờ và số khách;
   - một thông báo riêng về email: *đã gửi tới k\*\*\*h@example.com*, *chưa gửi được (lượt đặt bàn vẫn được ghi nhận)*, hoặc *không nhập email*.
   
   Thông tin được đọc bằng `TempData.Peek`, nên tải lại trang vẫn thấy mã đặt bàn.
5. Nhân viên mở **Chi tiết đặt bàn** sẽ thấy khu vực *Email xác nhận* (làm ở S2-09 Task 3): trạng thái gửi, số lần thử, thời điểm thử gần nhất, kết quả và lỗi. Khu vực này tách riêng với *Trạng thái đặt bàn*.

Chưa cấu hình SMTP (`Email:Host` để trống) thì email được ghi thành tệp `.html` và `.txt` trong `App_Data/emails`. Mở tệp `.html` bằng trình duyệt để xem email.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/026_BookingConfirmationEmail.sql` (mới). Cần migration 024 của S2-09 Task 3. |
| Service | `Services/Reservations/BookingConfirmationEmail.cs`, `Services/Reservations/BookingEmailDispatcher.cs` (mới); đăng ký trong `Program.cs` |
| Model | `Models/Reservations/BookingSuccessViewModel.cs` (mới) |
| Controller | `Controllers/Reservations/ReservationsController.cs`: `Create` đọc mã đặt bàn rồi gửi email; `Success` dựng trang xác nhận |
| View/CSS | `Views/Reservations/Success.cshtml`, `wwwroot/css/reservation-email.css` |
| Test | `tests/.../BookingConfirmationEmailTests.cs`, `tools/.../BookingConfirmationVerification.cs` |

## Demo

```powershell
cd D:\HeThongDatBan-GoiMon-Chinh
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project src/RestaurantManagement.Web
```

1. Đăng nhập, vào **Đặt bàn mới**, nhập email của bạn rồi đặt bàn.
2. Trang xác nhận hiện mã đặt bàn (ví dụ `3FA9C1`), ngày giờ, số khách và dòng *“Email xác nhận đã được gửi tới …”*.
3. Mở email:
   - đã cấu hình SMTP thì xem trong hộp thư;
   - chưa cấu hình thì mở tệp `.html` mới nhất trong `src/RestaurantManagement.Web/App_Data/emails`.
   
   Email phải có đúng mã đặt bàn, ngày giờ, số khách và địa chỉ quán.
4. Vào **Danh sách đặt bàn**, bấm vào mã vừa tạo. Khu vực *Email xác nhận* hiện *Đã gửi thành công, 1/4*.
5. Thử trường hợp gửi lỗi: đặt `Email:Host` thành một máy chủ không tồn tại, rồi đặt bàn lại. Trang xác nhận vẫn hiện mã đặt bàn và dòng *“Email xác nhận chưa gửi được…”*. Màn hình chi tiết hiện lỗi của lần gửi.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

### `BookingConfirmationEmailTests` (không cần database)

**Nội dung email**
- Email có mã đặt bàn, ngày giờ theo giờ Việt Nam kèm thứ trong tuần, số khách và địa chỉ quán, ở cả bản HTML lẫn bản chữ thường.
- Tên khách được mã hoá HTML.
- Thiếu dữ liệu quán thì email vẫn dùng được.
- Nội dung lưu kèm (`PayloadJson`) không có mã đặt bàn thì bị từ chối.

**Gửi email**
- Gửi thành công thì lưu `Sent`.
- Gửi lỗi, hết thời gian chờ hoặc nội dung lưu kèm bị hỏng thì lưu `Failed` kèm lỗi, không ném lỗi ra ngoài.
- Khách không nhập email thì không gửi gì.
- Lỗi database khi nhận email từ hàng đợi không làm hỏng lượt đặt bàn.

**Trang xác nhận**
- Luôn có đủ mã đặt bàn với cả 3 kết quả gửi email.
- Email hiển thị đã được che bớt.

### `BookingConfirmationVerification` (lệnh `verify`, HTTP thật, SQL Server thật, chạy 2 tiến trình web)

**Lần 1: gửi thành công (email ghi ra tệp)**
- Đặt bàn và gửi email xong trong vòng 60 giây.
- Trang xác nhận có mã 6 ký tự; tải lại trang vẫn còn mã.
- Đúng một email được gửi, có đủ mã, ngày giờ, số khách và địa chỉ quán.
- `EmailOutbox` lưu `Sent`, số lần thử 1, thời điểm gửi, kèm thông tin cho email.
- Màn hình chi tiết cho nhân viên hiện *Đã gửi thành công*, trạng thái đặt bàn vẫn là *Chờ xác nhận*.
- Đặt bàn không có email: vẫn có mã đặt bàn và không gửi email nào.

**Lần 2: SMTP trỏ tới cổng đóng**
- Lượt đặt bàn vẫn được lưu.
- Trang xác nhận vẫn có mã đặt bàn và thông báo email chưa gửi được.
- `EmailOutbox` lưu lần thử thất bại kèm lỗi; màn hình chi tiết hiện lỗi đó.
