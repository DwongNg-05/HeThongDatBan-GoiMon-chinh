# S4-08 Task 4: đường dẫn huỷ trong email nhắc theo điều kiện thời gian

Email nhắc có đường dẫn **Huỷ đặt bàn** riêng cho từng lượt khi còn **hơn 60 phút** tới giờ hẹn. Khách mở đường dẫn, xem lại thông tin, bấm **Xác nhận huỷ** và thấy kết quả.

**Hoàn tất:** AC 1, AC 2, AC 3, AC 4 (cùng Task 1–3, story S4-08 đã đủ).

## Quy định cần PO chốt (đề xuất, đã cài đặt)

| Câu hỏi | Đề xuất (đã cài đặt) | Lý do |
| --- | --- | --- |
| Điều kiện huỷ tính theo giờ gửi email hay thời điểm khách mở email? | **Cả hai, mỗi mốc một việc.** (1) **Có đường dẫn trong email hay không** tính theo **giờ gửi**: giờ hẹn − giờ gửi > 60 phút. (2) **Có được huỷ hay không** tính theo **thời điểm khách bấm Xác nhận huỷ**: lúc đó vẫn phải còn hơn 60 phút. | Nội dung email đã gửi thì không đổi được, nên việc có đường dẫn hay không chỉ có thể quyết định lúc gửi. Nếu chỉ dùng giờ gửi, khách mở email lúc còn 5 phút vẫn huỷ được, nhà hàng mất bàn sát giờ. Mốc 60 phút lúc bấm cũng khớp quy tắc tự huỷ ở trang **Tra cứu đặt bàn** (`CancelCutoffMinutes`). Nếu PO chọn chỉ tính theo giờ gửi, bỏ điều kiện `TooLate` trong `usp_CancelReservationFromReminder`. |
| Còn đúng 60 phút | **Không** có đường dẫn, **không** huỷ được ("hơn 60 phút" là > 60). | Theo mô tả task. |
| Số phút | `RestaurantSettings.CancelCutoffMinutes` (mặc định 60) trong database; `BookingReminderRules.CancelCutoffMinutes = 60` cho email. | Nếu đổi số phút trong cài đặt, cần đổi cả hằng số này. |

## Cách hoạt động

- **Đường dẫn riêng từng lượt**: `BookingConfirmationLinks.CreateCancelUrl(id, mã)` tạo `/Reservations/CancelFromEmail?token=...`. Mã bảo mật (ASP.NET Data Protection, mục đích riêng `BookingReminderCancel.v1`) bọc **Id + mã đặt bàn**. Không đoán hay sửa được mã; mã của nút "Xác nhận đặt bàn" cũng không dùng thay được.
- **Email nhắc** (`BookingReminderEmail`): khi đủ điều kiện có nút **Huỷ đặt bàn** kèm ghi chú "chỉ huỷ được khi còn hơn 60 phút". Khi không đủ điều kiện, email chỉ ghi "vui lòng gọi điện báo nhà hàng". Bản chữ thường cũng vậy.
- **Trang khách** (`GET /Reservations/CancelFromEmail`, không cần đăng nhập): hiện mã đặt bàn, giờ hẹn, bàn, số khách, trạng thái và nút **Xác nhận huỷ**. Nút chỉ hiện khi lượt còn huỷ được. Mở trang không huỷ gì, nên trình quét liên kết của hộp thư không thể tự huỷ hộ khách.
- **Xác nhận huỷ** (`POST`, có chống giả mạo) gọi `dbo.usp_CancelReservationFromReminder` (migration `062`). Thủ tục chạy dưới khoá chung, khớp **cả Id và mã đặt bàn**, kiểm tra lại điều kiện tại thời điểm bấm, rồi trả về:
  - `Cancelled`: huỷ thành công. Lượt chuyển **Đã huỷ**, ghi giờ huỷ, lý do "Khách huỷ qua email nhắc lịch" và lịch sử trạng thái. Email nhắc chưa gửi bị bỏ qua, bàn được trả lại, khách nhận email báo huỷ.
  - `AlreadyCancelled`: đã huỷ trước đó. Bấm lại hay gửi lại yêu cầu **không thay đổi dữ liệu**.
  - `Arrived`: khách đã tới.
  - `TooLate`: còn ≤ 60 phút.
  - `NotCancellable`: bị từ chối hoặc khách không tới.
  - `NotFound`: đường dẫn sai.

  Chỉ kết quả `Cancelled` thay đổi dữ liệu. Trang hiện thông báo thành công (xanh) hoặc lý do không huỷ được (vàng).
- Phân quyền: `Reservations.CancelFromEmail GET/POST` là trang công khai (đã thêm vào `ApiAccessMatrix`).

## Demo

1. Chạy `migrate` (áp dụng migration 062), rồi chạy web. Nếu demo bằng điện thoại, cấu hình `Booking:PublicBaseUrl`.
2. Tạo và xác nhận hai lượt có email: A hẹn sau khoảng 1 tiếng 40 phút, B hẹn sau khoảng 50 phút.
3. Quản lý mở **Email nhắc lịch** và bấm **Chạy kiểm tra nhắc lịch ngay**.
4. Mở email của A (hộp thư, hoặc tệp `.html` trong `App_Data/emails`): có nút **Huỷ đặt bàn**. Email của B **không có** nút này.
5. Bấm **Huỷ đặt bàn** trong email A. Trang hiện thông tin lượt và nút **Xác nhận huỷ**. Bấm xác nhận: thông báo "Đã huỷ lượt đặt bàn thành công", trạng thái **Đã huỷ**. Chi tiết đặt bàn phía nhân viên cũng hiện Đã huỷ.
6. Mở lại đường dẫn: trang báo "đã được huỷ trước đó", không còn nút xác nhận.

## Kiểm thử

- **Không cần database** (`BookingReminderTests.cs`):
  - gửi trước giờ hẹn 100 phút và 61 phút: email **có** đường dẫn (HTML và chữ thường);
  - gửi khi còn **đúng 60 phút** và 20 phút: **không** có;
  - đường dẫn đọc ra đúng Id và mã của lượt, mỗi lượt một đường dẫn khác nhau;
  - mã bị sửa, mã của nút xác nhận hoặc mã của máy chủ khác không huỷ được gì;
  - điều kiện huỷ theo thời điểm xác nhận (61 phút được, 60 phút không);
  - lượt đã huỷ, khách đã tới, khách không tới không huỷ được;
  - thông báo cho khách và nút xác nhận trên trang.
- **SQL Server thật** (`--reminder-sql`, file `BookingReminderSqlTests.cs`):
  - huỷ hợp lệ: một dòng lịch sử, một email báo huỷ;
  - **gửi lại yêu cầu huỷ hai lần không thay đổi dữ liệu** (cùng giờ huỷ, lịch sử, email);
  - Id hoặc mã không khớp không huỷ lượt khác;
  - còn 60 phút không huỷ được;
  - khách đã tới không huỷ được.
