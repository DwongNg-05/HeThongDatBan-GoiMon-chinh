# S4-08 Task 1: gửi email nhắc trước giờ hẹn 2 tiếng

Lượt đặt bàn **đã xác nhận** được gửi email nhắc khi còn 2 tiếng tới giờ hẹn. Email có **mã đặt bàn**, **giờ hẹn** và **bàn đã xếp**. Nhân viên xem được email nhận nhắc, trạng thái gửi và thời điểm gửi thành công trên chi tiết lượt đặt bàn, và xem toàn bộ email nhắc ở màn hình **Email nhắc lịch**.

**Hoàn tất:** một phần AC 1 (gửi nhắc đúng thời điểm cho lượt đã xác nhận), phần nội dung cơ bản của AC 2.
**Chưa làm (task sau):** liên kết huỷ trong email; loại trừ lượt đã huỷ hoặc khách đã tới sau khi email đã xếp hàng; cơ chế bảo đảm không gửi trùng khi tác vụ chạy lại hoặc web dừng giữa lúc gửi.

## Quy định cần PO chốt (đề xuất, đã cài đặt)

| Câu hỏi | Đề xuất (đã cài đặt) | Lý do |
| --- | --- | --- |
| Email nhận nhắc lấy từ thông tin khách hàng hay thông tin lượt đặt bàn? | **Từ lượt đặt bàn** (`dbo.Reservations.Email`). | Hệ thống không có bảng hay tài khoản khách hàng; khách nhập email riêng cho từng lượt đặt. Email xác nhận (S2-09) cũng gửi tới email này, nên khách nhận nhắc ở đúng hộp thư đã nhận xác nhận. Nếu sau này có hồ sơ khách hàng, chỉ cần đổi nguồn email trong `usp_QueueDueBookingReminders`. |
| "Còn đúng 2 tiếng" tính thế nào? | Lượt đến hạn khi giờ hẹn còn ở tương lai và **còn ≤ 2 tiếng**. Tác vụ chạy mỗi 60 giây, nên email đi trong khoảng 2 tiếng đến 1 tiếng 59 phút trước giờ hẹn. | Không thể khớp đúng từng giây; khoảng 1 phút là đủ chính xác cho khách. |
| Lượt được xác nhận khi chỉ còn dưới 2 tiếng? | Được nhắc ngay ở lượt chạy kế tiếp. | Khách vẫn cần nhắc mã đặt bàn và bàn. Nếu PO muốn bỏ qua các lượt này, thêm điều kiện "xác nhận trước mốc nhắc" vào thủ tục. |
| Số phút nhắc trước | 120 phút, lấy từ `dbo.RestaurantSettings.ReminderLeadMinutes` (đã có từ migration 001). | Đổi một chỗ trong database là đủ. |

## Cách hoạt động

- **Migration `059_S408BookingReminderEmail.sql`**
  - Thêm vào `dbo.Reservations`: `ReminderEmailStatus` (Pending / Sending / Retrying / Sent / Failed / Cancelled), `ReminderEmailSentAt` (thời điểm gửi thành công, UTC), `ReminderEmailUpdatedAt`.
  - `usp_QueueDueBookingReminders`: **tác vụ kiểm tra**. Tìm lượt `Confirmed`, có email, giờ hẹn trong (bây giờ, bây giờ + 120 phút], rồi xếp một email `BookingReminder` vào `dbo.EmailOutbox`. Email nhận, mã đặt bàn, giờ hẹn, bàn đã xếp, tên khách và thông tin quán được chụp lại trong `Recipient` và `PayloadJson`. Mỗi lượt có khoá `BookingReminder:{Id}` nên chỉ được xếp một lần.
  - `usp_ClaimDueBookingReminder`: nhận một email nhắc đang chờ gửi, chỉ khi giờ hẹn chưa qua. Gửi lỗi thì `usp_CompleteEmail` hẹn gửi lại sau 5 phút, tối đa 4 lần thử, giống các email đặt bàn khác.
  - `usp_SyncReservationEmailStatus` đồng bộ thêm trạng thái email nhắc lên lượt đặt bàn sau mỗi lần thử. Gửi thành công thì ghi `ReminderEmailSentAt = EmailOutbox.SentAt`.
  - `usp_RunMaintenance` dùng chung `usp_QueueDueBookingReminders`, nên email nhắc luôn có bàn đã xếp. Bản cũ chỉ lưu `TableId`.
- **Gửi email** (`BookingEmailDispatcher.SendDueRemindersAsync`): xếp hàng, rồi gửi mọi email nhắc đang chờ bằng mẫu `BookingReminderEmail`. Mẫu có bản HTML và bản chữ thường, tiêu đề dạng `Bếp Nhà - Nhắc lịch đặt bàn ED095C`, gồm mã đặt bàn, giờ hẹn (giờ Việt Nam), bàn, khu vực, số khách và địa chỉ quán. Khi chưa cấu hình SMTP (`Email:Host` trống), email được lưu thành tệp `.html`/`.txt` trong `App_Data/emails`.
- **Tác vụ chạy nền** `BookingReminderWorker`: chạy mỗi `Email:ReminderPollSeconds` giây (mặc định 60; đặt 0 để tắt).
- **Giao diện**
  - **Chi tiết đặt bàn** (`/Reservations/Details/{id}`) có khu vực **Email nhắc lịch** gồm: email nhận nhắc, mã đặt bàn, giờ hẹn, bàn đã xếp, mốc bắt đầu nhắc, trạng thái gửi nhắc và thời điểm gửi thành công. Email nhắc cũng xuất hiện trong lịch sử email bên dưới, với nhãn "Nhắc lịch đặt bàn" và từng lần thử.
  - **Email nhắc lịch** (`/ReservationReminders`, tab mới trong Quản lý đặt bàn; Quản lý và Phục vụ được xem) có bảng: mã đặt bàn, giờ hẹn, bàn, email nhận, trạng thái gửi, thời điểm gửi. Có bộ lọc **Chỉ email đã gửi thành công**. Quản lý có nút **Chạy kiểm tra nhắc lịch ngay**, dùng cho demo hoặc khi không muốn chờ lượt chạy 60 giây.
- Phân quyền: `ReservationReminders.Index GET` dành cho Quản lý và Phục vụ, `ReservationReminders.RunNow POST` chỉ dành cho Quản lý. Cả hai đã được thêm vào `ApiAccessMatrix`.

## Demo

1. Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- migrate` (áp dụng migration 059), rồi chạy web.
2. Khách đặt bàn ở form công khai, có nhập email, với giờ hẹn **trong vòng 2 tiếng tới** (ví dụ bây giờ là 10:05 thì chọn 12:00). Đặt thêm một lượt **sau 2 tiếng** (ví dụ 15:00).
3. Nhân viên mở **Đơn chờ xử lý**, chọn bàn và **Xác nhận** cả hai lượt.
4. Chờ tối đa 60 giây, hoặc Quản lý mở **Email nhắc lịch** và bấm **Chạy kiểm tra nhắc lịch ngay**.
5. Màn hình **Email nhắc lịch** có lượt 12:00 với trạng thái **Đã gửi thành công**, kèm mã đặt bàn, giờ hẹn, bàn, email nhận và giờ gửi. Lượt 15:00 **chưa có** email nhắc.
6. Mở email (hộp thư thật, hoặc tệp `.html` mới nhất trong `src/RestaurantManagement.Web/App_Data/emails`): email có mã đặt bàn, giờ hẹn và bàn đã xếp.
7. Bấm vào mã đặt bàn để mở chi tiết: khu vực **Email nhắc lịch** hiện email nhận nhắc, trạng thái "Đã gửi thành công" và thời điểm gửi.

## Kiểm thử

- **Không cần database** (`dotnet run --project tests/RestaurantManagement.AreaTests`, file `BookingReminderTests.cs`):
  - lượt đã xác nhận còn đúng 2 tiếng (và dưới 2 tiếng) được nhắc;
  - lượt còn hơn 2 tiếng, lượt đã qua giờ, lượt chưa xác nhận và lượt không có email không được nhắc;
  - email có đúng mã đặt bàn, giờ hẹn (giờ Việt Nam), bàn đã xếp; dữ liệu khách được mã hoá HTML;
  - tác vụ gửi đúng lượt, ghi thời điểm gửi thành công lên lượt đặt bàn; chạy lại ngay không gửi thêm; 1 phút sau lượt kế tiếp tới mốc thì được nhắc;
  - gửi lỗi ghi lỗi và không có thời điểm gửi;
  - danh sách email nhắc hiển thị đúng mã, giờ hẹn, bàn, email nhận, trạng thái, giờ gửi; bộ lọc "đã gửi";
  - nhãn trạng thái nhắc trên chi tiết đặt bàn.

  Kiểm thử phân quyền (`ApiAuthorizationCoverageTests`) tự rà hai API mới theo `ApiAccessMatrix`.
- **SQL Server** (`docs/S4-08-KiemTraNhacLich.sql`, chạy trong SSMS trên database dev sau khi `migrate`): tạo 4 lượt mẫu trong một giao dịch rồi **rollback**, kiểm tra:
  - lượt còn 1 tiếng 59 phút được xếp email nhắc tới email của lượt đặt;
  - lượt còn 2 tiếng 5 phút, lượt không email và lượt chờ xác nhận không được xếp;
  - nội dung email có mã đặt bàn, giờ hẹn và bàn;
  - trạng thái chuyển Pending → Sending → Sent và thời điểm gửi được ghi;
  - chạy lại không xếp thêm email;
  - 6 phút sau, lượt 2 tiếng 5 phút tới mốc thì được xếp.
