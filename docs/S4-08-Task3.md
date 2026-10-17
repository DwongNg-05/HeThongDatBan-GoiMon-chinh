# S4-08 Task 3: mỗi lượt đặt bàn chỉ nhận tối đa một email nhắc

Tác vụ nhắc lịch chạy lại (mỗi 60 giây, hoặc Quản lý bấm **Chạy kiểm tra nhắc lịch ngay**) hay hai tác vụ chạy cùng lúc (nhiều máy web, hoặc nút bấm trùng lượt chạy định kỳ) đều không gửi email nhắc thứ hai cho cùng một lượt đặt bàn.

**Hoàn tất:** AC 1, AC 3, AC 4. **Chưa làm (task sau):** đường dẫn huỷ trong email.

## Cơ chế (migration `061_S408OneReminderPerReservation.sql`)

| Lớp bảo vệ | Cách làm |
| --- | --- |
| Xác định lượt **đã được nhắc** | `Reservations.ReminderEmailSentAt`. Chỉ ghi khi `usp_CompleteEmail` báo **gửi thành công**, và đã ghi thì không bao giờ bị xoá. Gửi lỗi không ghi. |
| Loại lượt đã nhắc khỏi các lần chạy sau | `usp_QueueDueBookingReminders` và `usp_ClaimDueBookingReminder` chỉ lấy lượt có `ReminderEmailSentAt IS NULL`. Nếu lượt đã nhắc mà còn email nhắc đang chờ (dữ liệu bất thường), email đó bị huỷ. |
| Mỗi lượt tối đa **một** email nhắc | Chỉ mục duy nhất `UX_EmailOutbox_OneReminderPerReservation`, ngoài `DedupeKey = BookingReminder:{Id}` đã có. Chèn trùng bị database từ chối; thủ tục xếp hàng bỏ qua thay vì báo lỗi. |
| Hai tác vụ chạy đồng thời | Xếp hàng và nhận email đều chạy trong giao dịch giữ khoá chung `usp_LockOperations`. Nhận email chuyển `Pending → Processing` và tăng số lần thử trong cùng một câu lệnh, nên tác vụ thứ hai không nhận được email đang được gửi. |
| Ngay trước khi gửi | `usp_CheckBookingReminderBeforeSend` trả về `AlreadySent` nếu lượt đã được nhắc thành công, và `NotClaimed` nếu email không còn đúng lần thử tác vụ này giữ (khoá 2 phút đã hết hạn và tác vụ khác đã nhận lại). Cả hai trường hợp web **không gửi** và đếm là "chặn gửi trùng". |
| Gửi thất bại | Email trở lại chờ gửi, tự thử lại sau 5 phút, tối đa 4 lần thử (quy tắc S2-09). Lượt chưa bị đánh dấu đã nhắc nên vẫn được thử lại. |

`Reservations.ReminderEmailAttempts` lưu số lần thử gửi nhắc và được cập nhật sau mỗi lần thử.

Giới hạn còn lại: nếu máy chủ email đã nhận thư nhưng web mất kết nối database trước khi kịp ghi "thành công", lần thử đó bị coi là lỗi và có thể được gửi lại. SMTP không có cách xác nhận trùng, nên trường hợp này chỉ xảy ra khi database gặp sự cố đúng lúc gửi.

## Giao diện

- **Email nhắc lịch**: thêm cột **Số lần thử** (ví dụ `2/4`) và **Kết quả cuối cùng**, ví dụ "Gửi thành công ở lần thử 2", "Lỗi, sẽ tự gửi lại (đã thử 1/4)", "Thất bại sau 4 lần thử", "Đã bỏ qua, không gửi". Thông báo sau khi bấm chạy có thêm số lần gửi trùng đã chặn.
- **Chi tiết đặt bàn**, khu vực Email nhắc lịch: **Số lần thử gửi nhắc** (`n/4`) và ghi chú "đã nhắc thành công, không gửi lại".

## Demo

1. Chạy `migrate` (áp dụng migration 061), rồi chạy web.
2. Tạo và xác nhận một lượt đặt bàn có email, giờ hẹn trong vòng 2 tiếng tới.
3. Quản lý mở **Email nhắc lịch** và bấm **Chạy kiểm tra nhắc lịch ngay** nhiều lần liên tiếp. Có thể mở thêm một tab bấm cùng lúc.
4. Danh sách chỉ có **một** dòng cho lượt đó: "Đã gửi thành công", số lần thử `1/4`. Thư mục `App_Data/emails` chỉ có **một** email nhắc cho lượt này. Các lần bấm sau báo "Không có lượt đặt bàn nào đến giờ nhắc."
5. Mở chi tiết lượt: "Số lần thử gửi nhắc 1/4 · đã nhắc thành công, không gửi lại".

## Kiểm thử

- **Không cần database** (`BookingReminderTests.cs`, hàng đợi giả lập có khoá giống `usp_LockOperations`):
  - chạy tác vụ 6 lần liên tiếp: chỉ một email;
  - 4 lượt tác vụ chạy đồng thời với máy chủ email chậm: mỗi lượt đặt bàn đúng một email;
  - lượt đã nhắc không bị gửi lại, kể cả khi hàng đợi lỡ có email thứ hai;
  - khoá hết hạn và tác vụ khác đã nhận lại email: tác vụ cũ không gửi;
  - gửi thất bại không đánh dấu đã nhắc, sau 5 phút được thử lại và thành công, rồi không gửi lại;
  - số lần thử và kết quả cuối cùng trên danh sách.
- **SQL Server thật** (`BookingReminderSqlTests.cs`): tạo database tạm, chạy toàn bộ migration rồi xoá database tạm.

  ```powershell
  $env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
  dotnet run --project tests/RestaurantManagement.AreaTests -- --reminder-sql
  ```

  Bài test dùng đúng `SqlBookingEmailOutbox` của web cho hai "máy web" chạy đồng thời, rồi kiểm tra:
  - mỗi lượt đúng một email;
  - chạy thêm 5 lần không gửi thêm;
  - database từ chối email nhắc thứ hai;
  - gửi lỗi được thử lại một lần, thành công, và không gửi lại.
