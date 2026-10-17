# S4-08 Task 2: ngăn gửi nhắc cho lượt đặt bàn không còn hợp lệ

Email nhắc chỉ được gửi khi lượt đặt bàn **vẫn ở trạng thái đã xác nhận tại thời điểm gửi**. Lượt đã huỷ hoặc khách đã tới thì email nhắc bị **bỏ qua**, kèm lý do hiển thị trong danh sách theo dõi.

**Hoàn tất:** AC 1, AC 3. **Chưa làm (task sau):** đường dẫn huỷ trong email; cơ chế chống gửi trùng khi tác vụ chạy lại.

## Quy tắc

| Trạng thái lượt đặt bàn lúc gửi | Kết quả | Lý do bỏ qua (`SkipReason`) |
| --- | --- | --- |
| Đã xác nhận, chưa tới giờ hẹn | **Gửi** | – |
| Đã huỷ (hoặc lượt đã bị xoá) | Bỏ qua | `Cancelled`: "Lượt đặt bàn đã huỷ" |
| Khách đã tới | Bỏ qua | `Arrived`: "Khách đã tới" |
| Bị từ chối / Khách không tới | Bỏ qua | `Rejected` / `NoShow` |
| Chờ xác nhận (không còn ở trạng thái đã xác nhận) | Bỏ qua | `NotConfirmed` |
| Đã qua giờ hẹn | Bỏ qua | `StartPassed` |

Quy tắc nằm ở một chỗ trong database, `dbo.fn_BookingReminderSkipReason` (migration 060). `BookingReminderRules.SkipReasonFor` mô tả cùng quy tắc cho màn hình và kiểm thử.

## Cách hoạt động

- **Migration `060_S408SkipInvalidReminders.sql`**
  - Thêm `EmailOutbox.SkipReason` và `Reservations.ReminderSkipReason`. Email bị bỏ qua có `Status = 'Cancelled'` kèm lý do.
  - **Kiểm tra khi lấy danh sách email cần gửi**: `usp_SkipInvalidBookingReminders` bỏ qua mọi email nhắc đang chờ của lượt không còn hợp lệ. Thủ tục chạy ở đầu `usp_ClaimDueBookingReminder`, `usp_ClaimEmail` (worker chung) và `usp_RunMaintenance`. Sau đó `usp_ClaimDueBookingReminder` chỉ nhận email của lượt có `Status = 'Confirmed'`.
  - **Kiểm tra lại ngay trước khi gửi**: `usp_CheckBookingReminderBeforeSend`. Web đã nhận email nhưng lượt vừa bị huỷ hoặc khách vừa tới thì email bị bỏ qua và lần thử bị gỡ (không tính là một lần gửi). Web không gửi email đó.
  - `usp_MarkReservationArrived`: nhân viên đánh dấu khách đã tới (Đã xác nhận → Khách đã tới, ghi `ArrivedAt` và lịch sử trạng thái). Email nhắc chưa gửi của lượt bị bỏ qua ngay.
  - Email nhắc bị huỷ trước đây không có lý do (ví dụ do Quản lý huỷ lượt) được bổ sung lý do ở lượt chạy kế tiếp.
- **Tác vụ nhắc lịch** (`BookingEmailDispatcher.SendDueRemindersAsync`): nhận email → kiểm tra lại → gửi hoặc bỏ qua. Kết quả chạy có thêm số email bị bỏ qua.
- **Giao diện**
  - **Chi tiết đặt bàn**: trạng thái lượt đặt có nhãn màu riêng cho **Đã xác nhận**, **Đã huỷ** (kèm giờ huỷ) và **Khách đã tới** (kèm giờ tới). Khu vực **Email nhắc lịch** hiện thêm trạng thái lượt đặt và lý do bỏ qua, ví dụ "Đã bỏ qua gửi nhắc: Khách đã tới". Lượt đã xác nhận có nút **Đánh dấu khách đã tới** (Quản lý, Phục vụ).
  - **Email nhắc lịch** (danh sách theo dõi): thêm cột **Trạng thái lượt đặt** và **Lý do bỏ qua**, bộ lọc **Chỉ email bị bỏ qua**, và số email bị bỏ qua.
- Phân quyền: `Reservations.MarkArrived POST` dành cho Quản lý và Phục vụ (đã thêm vào `ApiAccessMatrix`).

## Demo

1. Chạy `migrate` (áp dụng migration 060), rồi chạy web.
2. Tạo và xác nhận 3 lượt đặt bàn có email, giờ hẹn trong vòng 2 tiếng tới (A, B, C).
3. Quản lý mở chi tiết lượt A và **Huỷ đặt bàn**. Mở chi tiết lượt B và bấm **Đánh dấu khách đã tới**. Nhãn trạng thái đổi thành **Đã huỷ** và **Khách đã tới**.
4. Mở **Email nhắc lịch** và bấm **Chạy kiểm tra nhắc lịch ngay**.
5. Chỉ lượt C có email mới (trạng thái "Đã gửi thành công"). Nếu A, B đã được xếp hàng nhắc trước khi đổi trạng thái, hai dòng này hiện "Đã bỏ qua" với lý do **Lượt đặt bàn đã huỷ** và **Khách đã tới**. Thư mục `App_Data/emails` không có email mới cho A, B.
6. Chọn bộ lọc **Chỉ email bị bỏ qua** để xem riêng các email bị bỏ qua. Mở chi tiết A hoặc B: khu vực Email nhắc lịch ghi rõ lý do bỏ qua.

## Kiểm thử

- **Không cần database** (`BookingReminderTests.cs`):
  - quy tắc lý do bỏ qua cho từng trạng thái;
  - lượt đã huỷ không nhận email;
  - lượt khách đã tới không nhận email;
  - lượt vẫn đã xác nhận nhận email;
  - lượt đổi trạng thái ngay trước lúc gửi không nhận email và không tính lần gửi;
  - chạy lại không gửi thêm;
  - danh sách theo dõi hiện đúng lý do bỏ qua, bộ lọc "bị bỏ qua";
  - nhãn trạng thái Đã xác nhận / Đã huỷ / Khách đã tới trên chi tiết.
- **SQL Server** (`docs/S4-08-KiemTraBoQuaNhac.sql`, chạy trong SSMS, luôn rollback): cùng các tình huống trên với thủ tục thật. Có cả bước đổi trạng thái sau khi web đã nhận email, rồi kiểm tra `usp_CheckBookingReminderBeforeSend` trả về `Cancelled` và email không được tính lần gửi.
