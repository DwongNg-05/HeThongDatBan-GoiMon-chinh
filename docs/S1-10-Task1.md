# S1-10 Task 1 — Giờ hoạt động

Quy tắc đã chốt: mỗi ngày một khoảng giờ, thứ Hai = 1 đến Chủ nhật = 7. Ngày hoạt động bắt buộc có giờ đóng sau giờ mở trong cùng ngày; không hỗ trợ qua đêm. Ngày nghỉ bỏ qua các ô giờ và lưu giờ là NULL. Khi bỏ ngày nghỉ, quản lý nhập lại giờ.

Màn hình `/OpeningHours` có liên kết **Giờ hoạt động** trên thanh điều hướng. Dùng bảng `OpeningHours` và dữ liệu 7 ngày đã có từ migration 005. Migration 013 bổ sung kiểu bảng và thủ tục lưu toàn tuần trong một giao dịch; dữ liệu sai bị từ chối trước khi cập nhật. Không sửa migration cũ.

## Chạy demo

```powershell
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project src/RestaurantManagement.Web
```

Mở **Giờ hoạt động**, nhập 09:00–22:00, đánh dấu Chủ nhật nghỉ và lưu. Mở lại màn hình để kiểm tra dữ liệu. Đổi giờ đóng thứ Hai thành 09:00 hoặc 08:00, đồng thời thay đổi một ngày khác: có lỗi và cả tuần không được lưu. Tải lại để thấy cấu hình trước đó.

Ứng dụng chưa có đăng nhập web. `OpeningHours:ActorUserId` là danh tính demo cấu hình phía máy chủ, mặc định 1; tài khoản phải tồn tại, đang hoạt động và có quyền `Catalog.Manage`. Cần nối danh tính đã xác thực khi triển khai chức năng đăng nhập; hiện chưa phải phân quyền web hoàn chỉnh.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build -- --opening-hours-http
```

Kiểm thử HTTP tạo và dọn database tạm riêng; bao gồm lưu hợp lệ, ngày nghỉ, tải lại, giờ bằng nhau, giờ ngược, sai định dạng, thiếu giờ, ngày trùng và chống giả mạo yêu cầu. Các yêu cầu sai phải giữ nguyên toàn bộ dữ liệu đã lưu.

Phạm vi chỉ gồm giờ mở/đóng tuần và ngày nghỉ tuần; không bổ sung khung giờ nhận đặt bàn, thời lượng giữ bàn hay ngày lễ đặc biệt.
