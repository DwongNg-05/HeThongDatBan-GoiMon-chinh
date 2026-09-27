# S1-10 Task 4 — Kiểm tra lịch khi đặt bàn

Quy tắc thống nhất với Task 2: nhận bàn từ giờ mở (bao gồm) đến trước giờ đóng (không bao gồm), tăng từng 30 phút tính từ giờ mở. Ví dụ 08:00–22:00 cho phép 21:30 dù giữ bàn 90 phút; 06:15–09:20 cho phép 06:15, 06:45…09:15. Giây và phần lẻ giây không hợp lệ.

Migration 017 thêm `usp_ValidateBookingSchedule`, dùng chung cho kiểm tra khi khách đổi ngày giờ và thao tác tạo đặt bàn. Khi lưu, kiểm tra được chạy lại trong giao dịch có khóa nghiệp vụ, trước khi tạo đặt bàn/sự kiện/email. Ngày nghỉ đặc biệt đang áp dụng ưu tiên hơn lịch tuần; ngày nghỉ tuần, ngoài giờ, sai bước hoặc thời điểm quá khứ đều bị từ chối với thông báo riêng. Yêu cầu hợp lệ tiếp tục luồng Pending hiện có.

Nguồn lịch nhận bàn là `OpeningHours` và `SpecialHolidays`, thống nhất với màn hình lịch Task 3. Luồng tạo mới không còn dùng bảng `SpecialDates` cũ hoặc giờ cố định trên controller. Đặt bàn đã có không bị tự động hủy. Chưa thay đổi luồng xác nhận/phân bàn.

Form kiểm tra bằng yêu cầu chỉ đọc ngay khi đổi ngày giờ. Nếu mạng lỗi hoặc JavaScript bị tắt, kiểm tra khi gửi vẫn bắt buộc tại database. Kết quả xem trước không thay thế kiểm tra lúc lưu.

Giờ nhập được hiểu là giờ Việt Nam; giữ chuyển đổi UTC hiện có và UTC+7 trong SQL, chưa triển khai đầy đủ bài toán múi giờ toàn hệ thống.

## Demo

Sau migrate và khởi động lại web, cấu hình 08:00–22:00. Trên form Đặt bàn, thử 08:00, 21:30 (hợp lệ), 07:30, 22:00, 22:30, 08:15 (bị từ chối). Đánh dấu nghỉ tuần hoặc thêm ngày nghỉ đặc biệt rồi thử gửi lại. Thử mở 06:15–09:20 để xác minh không còn giới hạn giờ cứng và bước 30 phút bắt đầu từ 06:15.

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --opening-hours-http
```

Kiểm thử dùng database tạm riêng, kiểm tra phản hồi tức thời và POST, các mốc biên, lịch đổi sau xem trước, ngày nghỉ, giờ lệch mốc, không tạo dữ liệu khi sai và kiểm tra trực tiếp SQL.
