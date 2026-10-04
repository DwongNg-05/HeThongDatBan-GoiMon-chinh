# Khách tự chọn bàn: mã bàn là mã khách nhận

## Yêu cầu

- Mã khách nhận khi đặt bàn phải là mã của một bàn có thật trong quán, ví dụ `A05`.
- Quản lý, phục vụ, thu ngân và bếp đều xem được thông tin khách đã đặt trước: mã bàn, giờ, số khách, ghi chú…

## Cách hoạt động

1. **Chọn bàn khi đặt.** Form đặt bàn có thêm ô **Chọn bàn**, bắt buộc phải chọn.
   - Ô này chỉ liệt kê bàn còn trống theo giờ, số khách và khu vực đã chọn, mỗi dòng có dạng “A05 · Tầng 1 · tối đa 4 khách”.
   - Khi khách đổi giờ, số khách hoặc khu vực, danh sách tự tải lại qua `GET /Reservations/AvailableTables`.
2. **Bàn còn trống** là bàn:
   - đang hoạt động, thuộc khu vực đang hoạt động;
   - có `MaxCapacity` ≥ số khách;
   - chưa có lượt đặt nào ở trạng thái *Chờ xác nhận*, *Đã xác nhận* hoặc *Khách đã đến* trùng khung giờ (giờ bắt đầu + thời lượng mặc định).
3. **Kiểm tra lại khi gửi form.** `usp_CreateReservation` (migration **027**) kiểm tra lại bàn trong lúc giữ khoá `usp_LockOperations`, nên hai khách không thể cùng giữ một bàn. Có hai lỗi mới:
   - `51413`: bàn không còn sử dụng, không đủ chỗ, hoặc không thuộc khu vực đã chọn;
   - `51414`: bàn vừa có khách khác đặt.
   
   Cả hai lỗi đều hiện ngay dưới ô **Chọn bàn**.
4. **Lưu bàn vào lượt đặt.** Lượt đặt ở trạng thái *Chờ xác nhận* lưu luôn bàn đã chọn (`TableId`). Nếu khách không chọn khu vực, khu vực của bàn được dùng thay.
5. **Mã đặt bàn chính là mã bàn.** Trang xác nhận, email (tiêu đề `Xác nhận đặt bàn A05 – Bếp Nhà`), danh sách và chi tiết đặt bàn chỉ hiện **một mã** là mã bàn, ví dụ `A05`.
   - Cột `Reservations.Code` (6 ký tự) vẫn được giữ trong database làm khoá nội bộ, vì cùng một bàn có nhiều lượt đặt ở các giờ khác nhau, nhưng không hiển thị cho khách và nhân viên.
   - Lượt đặt cũ chưa có bàn hiện “Chưa có bàn” trong danh sách và dùng mã 6 ký tự trên trang xác nhận.
6. **Nhân viên xem lượt đặt.**
   - **Danh sách đặt bàn**: cột *Mã đặt bàn (mã bàn)* và *Ghi chú*.
   - **Chi tiết đặt bàn**: *Mã đặt bàn (mã bàn)* kèm khu vực của bàn, và *Ghi chú*.
   - Danh sách, chi tiết và khu vực trạng thái email chỉ mở cho các vai trò có quyền `Reservations.Read`: **Quản lý, Phục vụ, Bếp, Thu ngân** (`[Authorize(Roles = "Manager,Waiter,Kitchen,Cashier")]`).
   - Bếp và thu ngân chỉ xem, vì các trang này không có chức năng sửa.

## Tương thích

- Gọi `usp_CreateReservation` mà không truyền `@TableId` thì vẫn tạo lượt *Chờ xác nhận* chưa có bàn như trước. Nhờ đó kiểm thử cũ, dữ liệu nhập tay và luồng “đặt khu vực chưa có bàn” không bị ảnh hưởng.
  - Trên giao diện, ô **Chọn bàn** là bắt buộc nên khách luôn phải chọn bàn.
  - Lượt đặt chưa có bàn hiển thị “Chưa có bàn” trong danh sách, và dùng mã đặt bàn 6 ký tự làm mã cho khách.
- `usp_ConfirmReservation` (nhân viên xác nhận hoặc đổi bàn) **chưa được sửa**. Thủ tục này hiện chỉ kiểm tra trùng giờ với các lượt *Đã xác nhận* và *Khách đã đến*. Nếu cần chặn cả trường hợp xếp bàn đè lên bàn mà khách khác đang giữ (*Chờ xác nhận*), cần bổ sung ở task sau.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `027_CustomerChoosesTable.sql`: thêm `usp_AvailableTables`, thêm `@TableId` cho `usp_CreateReservation`, thêm `TableCode` vào nội dung lưu kèm và tiêu đề email (`usp_QueueBookingEmail`) |
| Model | `ReservationViewModels.cs`: thêm `TableId`, `Tables`, `BookingTableOption`; thêm `TableCode`, `TableAreaName`, `Notes` vào danh sách; `BookingSuccessViewModel`: thêm `TableCode`, `DisplayCode` |
| Controller | `ReservationsController`: thêm action `AvailableTables`, truyền `@TableId`, đọc `TableCode`, phân quyền xem |
| View/JS | `Create.cshtml` (ô **Chọn bàn**), `booking-tables.js`, `Success.cshtml`, `Index.cshtml`, `Details.cshtml` |
| Email | `BookingConfirmationEmail.cs`: hiện mã bàn trong tiêu đề, ô mã to và bản chữ thường |
| Test | `BookingConfirmationEmailTests` (mã bàn trong email và trang xác nhận, quyền xem), `BookingConfirmationVerification` (chọn bàn, bàn biến khỏi danh sách trống, không đặt trùng bàn, bếp/thu ngân xem được) |
