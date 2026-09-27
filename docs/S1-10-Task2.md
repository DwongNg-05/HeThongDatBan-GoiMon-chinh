# S1-10 Task 2 — Khung giờ nhận đặt bàn

Quy tắc đã chốt với người dùng: giờ nhận bàn phải **trước** giờ đóng; không trừ thời lượng giữ bàn khỏi giờ đóng. Ví dụ 08:00–22:00 tạo 28 lượt từ 08:00 đến 21:30, dù giữ bàn 90 hay 120 phút. Các lượt bắt đầu đúng giờ mở và tăng 30 phút: 08:15–09:20 cho 08:15, 08:45, 09:15. Khoảng mở dưới 30 phút vẫn có một lượt tại giờ mở. Ngày nghỉ không có lượt.

Màn hình `/OpeningHours` hiển thị lượt cho từng ngày, cập nhật ngay khi nhập và vẫn hiển thị từ máy chủ khi không có JavaScript. Thời lượng giữ bàn mặc định 90 phút, sửa được trong khoảng 30–360 phút (số nguyên, theo ràng buộc database hiện có). Lượt chỉ được tính từ giờ mở/đóng, không phụ thuộc thời lượng.

Migration 014 cập nhật thủ tục lưu: cả tuần và `RestaurantSettings.DefaultBookingMinutes` được lưu trong một giao dịch. Dữ liệu giờ hoặc thời lượng không hợp lệ không được lưu. Giá trị cấu hình hiện có được giữ nguyên khi nâng cấp; không đặt lại thành 90.

## Demo

Chạy migrate theo README, khởi động web và mở **Giờ hoạt động**. Nhập 08:00–22:00: danh sách kết thúc ở 21:30. Đổi giữ bàn thành 120, đánh dấu Chủ nhật nghỉ, lưu rồi mở lại. Thử giờ 08:15–09:20 để thấy bước 30 phút tính từ giờ mở. Nhập thời lượng 29 hoặc giờ đóng bằng giờ mở: không lưu được.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --opening-hours-http
```

Đặt `RM_CONNECTION_STRING` như README trước khi chạy kiểm thử HTTP. Bộ kiểm thử dùng database tạm riêng; kiểm tra bước 30 phút, mặc định 90, thay đổi và tải lại thời lượng, ngày nghỉ, giờ lẻ, gần nửa đêm, loại giờ đóng khỏi lượt, dữ liệu sai không thay đổi cả tuần hoặc thời lượng.

Không thêm ngày lễ hay thay đổi chức năng kiểm tra yêu cầu đặt bàn thực tế. Các quy tắc đặt bàn SQL có sẵn từ trước vẫn giữ nguyên; danh sách này là cấu hình/hiển thị theo AC Task 2. Web vẫn dùng danh tính demo phía máy chủ như Task 1, chưa có đăng nhập hoàn chỉnh.
